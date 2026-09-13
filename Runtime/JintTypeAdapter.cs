using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Jint.Runtime.Modules;
using Nox.CCK.Scripting;
using Nox.CCK.Utils;
using Nox.Scripting;
using JintEngine = Jint.Engine;
using NoxLogger = Nox.CCK.Utils.Logger;

namespace Nox.Jint.Runtime {
	public static class JintTypeAdapter {
		public static ObjectInstance BuildInstance(
			JintEngine              engine,
			IScriptingTypeConverter converter,
			object                  instance,
			IJintScriptingContext   ctx
		) {
			var obj = engine.Intrinsics.Object.Construct(Array.Empty<JsValue>(), engine.Intrinsics.Object);
			obj.DefineOwnProperty("Name", new PropertyDescriptor(converter.HandledType.Name, writable: false, enumerable: false, configurable: false));
			// Store the original .NET instance so FromJsValue can unwrap it back
			obj.DefineOwnProperty("__target", new PropertyDescriptor(JsValue.FromObject(engine, instance), writable: false, enumerable: false, configurable: false));
			try {
				var events = new Dictionary<string, IScriptingTypeEventDefinition>();
				foreach (var binding in converter.Bindings) {
					var name = binding.Name.Resolve(NameResolver.camelCaseStyle);
					switch (binding) {
						case IScriptingTypeProperty property: {
							if (property.Setter == null || property.Flags.HasFlag(ScriptingTypePropertyFlags.IsReadOnly)) {
								var getter = new ClrFunction(engine, "get", (thisObj, _) => ToValue(engine, property.Getter(ctx, instance), ctx));
								obj.DefineOwnProperty(name, new GetSetPropertyDescriptor(getter, null, enumerable: true, configurable: false));
							} else {
								var getter = new ClrFunction(engine, "get", (thisObj, _) => ToValue(engine, property.Getter(ctx, instance), ctx));
								var setter = new ClrFunction(engine, "set", (_, args) => {
									property.Setter(ctx, instance, FromValue(args[0]));
									return JsValue.Undefined;
								});
								obj.DefineOwnProperty(name, new GetSetPropertyDescriptor(getter, setter, enumerable: true, configurable: true));
							}
							break;
						}
						case IScriptingTypeSyncMethod method: {
							var fn = new ClrFunction(engine, name, (_, args) => {
								var nativeArgs = ConvertArgs(args);
								try { return ToValue(engine, method.Handler(ctx, instance, nativeArgs), ctx); } catch (Exception e) {
									NoxLogger.LogError($"{converter.HandledType.Name}.{name}: {e.Message}", tag: nameof(JintTypeAdapter));
									return ToValue(engine, e, ctx);
								}
							});
							obj.DefineOwnProperty(name, new PropertyDescriptor(fn, writable: true, enumerable: true, configurable: true));
							break;
						}
						case IScriptingTypeAsyncMethod method: {
							var fn = new ClrFunction(engine, name, (_, args) => {
								var          nativeArgs = ConvertArgs(args);
								UniTask<object> task;
								try { task = method.Handler(ctx, instance, nativeArgs); } catch (Exception e) {
									NoxLogger.LogError($"{converter.HandledType.Name}.{name}: {e.Message}", tag: nameof(JintTypeAdapter));
									return ToValue(engine, e, ctx);
								}
								return ToValue(engine, task, ctx);
							});
							obj.DefineOwnProperty(name, new PropertyDescriptor(fn, writable: true, enumerable: true, configurable: true));
							break;
						}
						case IScriptingTypeEventDefinition evt: {
							var eventName = binding.Name.Resolve(NameResolver.camelCaseStyle);
							events[eventName] = evt;
							
							// Create getter/setter for the event property that returns an object with on/off/once/emit methods
							var getter = new ClrFunction(engine, "get", (thisObj, _) => {
								var eventObj = engine.Intrinsics.Object.Construct(Array.Empty<JsValue>(), engine.Intrinsics.Object);
								
								// on(...) method
								var onFn = new ClrFunction(engine, "on", (_, args) => {
									if (args.Length == 0 || !args[0].IsCallable())
										return JsValue.Undefined;
									
									evt.AddHandler(ctx, instance, (callbackArgs) => {
										var jsArgs = new JsValue[callbackArgs.Length];
										for (int i = 0; i < callbackArgs.Length; i++)
											jsArgs[i] = ToValue(engine, callbackArgs[i], ctx);
										var jsThis = ToValue(engine, instance, ctx);
										args[0].Call(jsThis, jsArgs);
									});
									
									return JsValue.Undefined;
								});
								
								// off(...) method
								var offFn = new ClrFunction(engine, "off", (_, args) => {
									evt.RemoveHandler(ctx, instance, null);
									return JsValue.Undefined;
								});
								
								// once(...) method
								var onceFn = new ClrFunction(engine, "once", (_, args) => {
									if (args.Length == 0 || !args[0].IsCallable())
										return JsValue.Undefined;

									void onceAction(object[] callbackArgs) {
										try {
											var jsArgs = new JsValue[callbackArgs.Length];
											for (int i = 0; i < callbackArgs.Length; i++)
												jsArgs[i] = ToValue(engine, callbackArgs[i], ctx);
											var jsThis = ToValue(engine, instance, ctx);
											args[0].Call(jsThis, jsArgs);
										} finally {
											evt.RemoveHandler(ctx, instance, onceAction);
										}
									}

									evt.AddHandler(ctx, instance, onceAction);
									return JsValue.Undefined;
								});
								
								var emitFn = new ClrFunction(engine, "emit", (_, args) => {
									var nativeArgs = ConvertArgs(args);
									evt.Emit(ctx, instance, nativeArgs);
									return JsValue.Undefined;
								});
									
								eventObj.Set("on", onFn, true);
								eventObj.Set("off", offFn, true);
								eventObj.Set("once", onceFn, true);
								eventObj.Set("emit", emitFn, true);

								return eventObj;
							});
							
							obj.DefineOwnProperty(eventName, new GetSetPropertyDescriptor(getter, null, enumerable: true, configurable: true));
							break;
						}
					}
				}

				// Direct event API: socket.on("data", handler)
				obj.Set("on", CreateDirectEventMethod(engine, events, ctx, instance, once: false), true);
				obj.Set("once", CreateDirectEventMethod(engine, events, ctx, instance, once: true), true);
				obj.Set("off", new ClrFunction(engine, "off", (_, args) => {
					if (args.Length == 0 || args[0].IsUndefined() || args[0].IsNull())
						return JsValue.Undefined;
					var eventName = args[0].ToString();
					if (events.TryGetValue(eventName, out var evt))
						evt.RemoveHandler(ctx, instance, null);
					return JsValue.Undefined;
				}), true);
				obj.Set("emit", new ClrFunction(engine, "emit", (_, args) => {
					if (args.Length == 0 || args[0].IsUndefined() || args[0].IsNull())
						return JsValue.Undefined;
					var eventName = args[0].ToString();
					if (!events.TryGetValue(eventName, out var evt))
						return JsValue.Undefined;
					var nativeArgs = new object[Math.Max(0, args.Length - 1)];
					for (var i = 1; i < args.Length; i++)
						nativeArgs[i - 1] = FromValue(args[i]);
					evt.Emit(ctx, instance, nativeArgs);
					return JsValue.Undefined;
				}), true);
			} catch (Exception e) {
				NoxLogger.LogError($"{nameof(BuildInstance)}({converter.HandledType.Name}): {e.Message}", tag: nameof(JintTypeAdapter));
			}
			return obj;
		}

		private static ClrFunction CreateDirectEventMethod(
			JintEngine engine,
			Dictionary<string, IScriptingTypeEventDefinition> events,
			IJintScriptingContext ctx,
			object instance,
			bool once
		) {
			return new ClrFunction(engine, once ? "once" : "on", (thisObj, args) => {
				if (args.Length < 2 || args[0].IsUndefined() || args[0].IsNull() || !args[1].IsCallable())
					return JsValue.Undefined;

				var eventName = args[0].ToString();
				if (!events.TryGetValue(eventName, out var evt))
					return JsValue.Undefined;

				var handler = args[1];
				void callback(object[] callbackArgs) {
					try {
						var jsArgs = new JsValue[callbackArgs.Length];
						for (var i = 0; i < callbackArgs.Length; i++)
							jsArgs[i] = ToValue(engine, callbackArgs[i], ctx);
						
						var jsThis = thisObj.IsUndefined() || thisObj.IsNull() ? ToValue(engine, instance, ctx) : thisObj;
						handler.Call(jsThis, jsArgs);
					} catch (Exception ex) {
						NoxLogger.LogWarning($"Event '{eventName}' handler failed: {ex.Message}", tag: nameof(JintTypeAdapter));
					} finally {
						if (once)
							evt.RemoveHandler(ctx, instance, callback);
					}
				}

				evt.AddHandler(ctx, instance, callback);
				return JsValue.Undefined;
			});
		}

		public static void BindModule(JintEngine engine, ModuleBuilder builder, IScriptingModuleDefinition module, IJintScriptingContext ctx) {
			try {
				var ns = engine.Intrinsics.Object.Construct(Array.Empty<JsValue>(), engine.Intrinsics.Object);

				foreach (var binding in module.Bindings) {
					switch (binding) {
						case IScriptingPropertyDefinition property: {
							var name = binding.Name.Resolve(NameResolver.camelCaseStyle);
							builder.ExportValue(name, ToValue(engine, property.Getter(ctx), ctx));
							var nsGetter = new ClrFunction(engine, "get", (_, _) => ToValue(engine, property.Getter(ctx), ctx));
							ClrFunction nsSetter = null;
							if (property.Setter != null)
								nsSetter = new ClrFunction(engine, "set", (_, args) => {
									property.Setter(ctx, FromValue(args.Length > 0 ? args[0] : JsValue.Undefined));
									return JsValue.Undefined;
								});
							ns.DefineOwnProperty(name, new GetSetPropertyDescriptor(nsGetter, nsSetter, enumerable: true, configurable: false));
							break;
						}
						case IScriptingSyncMethodDefinition method: {
							var name = binding.Name.Resolve(NameResolver.camelCaseStyle);
							var fn   = new ClrFunction(engine, name, (_, args) => {
								var nativeArgs = ConvertArgs(args);
								try { return ToValue(engine, method.Handler(ctx, nativeArgs), ctx); } catch (Exception e) {
									NoxLogger.LogError($"{module.Id.Resolve(NameResolver.snake_case_style)}.{name}: {e.Message}", tag: nameof(JintTypeAdapter));
									return ToValue(engine, e, ctx);
								}
							});
							builder.ExportValue(name, fn);
							ns.Set(name, fn, true);
							break;
						}
						case IScriptingAsyncMethodDefinition method: {
							var name = binding.Name.Resolve(NameResolver.camelCaseStyle);
							var fn   = new ClrFunction(engine, name, (_, args) => {
								var          nativeArgs = ConvertArgs(args);
								UniTask<object> task;
								try { task = method.Handler(ctx, nativeArgs); } catch (Exception e) {
									NoxLogger.LogError($"{module.Id.Resolve(NameResolver.snake_case_style)}.{name}: {e.Message}", tag: nameof(JintTypeAdapter));
									return ToValue(engine, e, ctx);
								}
								return ToValue(engine, task, ctx);
							});
							builder.ExportValue(name, fn);
							ns.Set(name, fn, true);
							break;
						}
						case IScriptingTypeConverterDefinition typeDef: {
							var name    = binding.Name.Resolve(NameResolver.PascalCaseStyle);
							var typeObj = BuildType(engine, typeDef.Converter, ctx);
							builder.ExportValue(name, typeObj);
							ns.Set(name, typeObj, true);
							break;
						}
					}
				}

				builder.ExportValue("default", ns);
			} catch (Exception e) {
				NoxLogger.LogError($"{nameof(BindModule)}({module.Id.Resolve(NameResolver.snake_case_style)}): {e.Message}", tag: nameof(JintTypeAdapter));
			}
		}

		public static ObjectInstance BuildType(JintEngine engine, IScriptingTypeConverter converter, IJintScriptingContext ctx) {
			var obj = engine.Intrinsics.Object.Construct(Array.Empty<JsValue>(), engine.Intrinsics.Object);
			obj.DefineOwnProperty("Name", new PropertyDescriptor(converter.HandledType.Name, writable: false, enumerable: false, configurable: false));
			try {
				if (converter.Constructor != null) {
					var constructorFn = new ClrFunction(engine, converter.HandledType.Name, (_, args) => {
						try { return ToValue(engine, converter.Constructor(ctx, ConvertArgs(args)), ctx); } catch (Exception e) {
							NoxLogger.LogError($"{converter.HandledType.Name} constructor: {e.Message}", tag: nameof(JintTypeAdapter));
							return ToValue(engine, e, ctx);
						}
					});
					obj.DefineOwnProperty(converter.HandledType.Name, new PropertyDescriptor(constructorFn, writable: true, enumerable: false, configurable: true));
					obj.DefineOwnProperty("from", new PropertyDescriptor(constructorFn, writable: true, enumerable: false, configurable: true));
				}
				foreach (var binding in converter.StaticBindings) {
					var name = binding.Name.Resolve(NameResolver.camelCaseStyle);
					switch (binding) {
						case IScriptingTypeProperty property: {
							if (property.Setter == null || property.Flags.HasFlag(ScriptingTypePropertyFlags.IsReadOnly)) {
								var getter = new ClrFunction(engine, "get", (thisObj, _) => ToValue(engine, property.Getter(ctx, null), ctx));
								obj.DefineOwnProperty(name, new GetSetPropertyDescriptor(getter, null, enumerable: true, configurable: false));
							} else {
								var getter = new ClrFunction(engine, "get", (thisObj, _) => ToValue(engine, property.Getter(ctx, null), ctx));
								var setter = new ClrFunction(engine, "set", (_, args) => {
									property.Setter(ctx, null, FromValue(args[0]));
									return JsValue.Undefined;
								});
								obj.DefineOwnProperty(name, new GetSetPropertyDescriptor(getter, setter, enumerable: true, configurable: true));
							}
							break;
						}
						case IScriptingTypeSyncMethod method: {
							var fn = new ClrFunction(engine, name, (_, args) => {
								var nativeArgs = ConvertArgs(args);
								try { return ToValue(engine, method.Handler(ctx, null, nativeArgs), ctx); } catch (Exception e) {
									NoxLogger.LogError($"{converter.HandledType.Name}.{name}: {e.Message}", tag: nameof(JintTypeAdapter));
									return ToValue(engine, e, ctx);
								}
							});
							obj.DefineOwnProperty(name, new PropertyDescriptor(fn, writable: true, enumerable: true, configurable: true));
							break;
						}
						case IScriptingTypeAsyncMethod method: {
							var asyncFn = new ClrFunction(engine, name, (_, args) => {
								var          nativeArgs = ConvertArgs(args);
								UniTask<object> task;
								try { task = method.Handler(ctx, null, nativeArgs); } catch (Exception e) {
									NoxLogger.LogError($"{converter.HandledType.Name}.{name}: {e.Message}", tag: nameof(JintTypeAdapter));
									return ToValue(engine, e, ctx);
								}
								return ToValue(engine, task, ctx);
							});
							obj.DefineOwnProperty(name, new PropertyDescriptor(asyncFn, writable: true, enumerable: true, configurable: true));
							break;
						}
					}
				}
			} catch (Exception e) {
				NoxLogger.LogError($"{nameof(BuildType)}({converter.HandledType.Name}): {e.Message}", tag: nameof(JintTypeAdapter));
			}
			return obj;
		}

		private static JsValue ToArray(JintEngine engine, Array list, IJintScriptingContext context = null) {
			if (list.Length == 0)
				return engine.Intrinsics.Array.Construct(0);
			var arr = engine.Intrinsics.Array.Construct(list.Length);
			for (var i = 0; i < list.Length; i++)
				arr[(uint)i] = ToValue(engine, list.GetValue(i), context);
			return arr;
		}

		private static JsValue ToEnumerable(JintEngine engine, IEnumerable enumerable, IJintScriptingContext context) {
			var items = new List<object>();
			foreach (var item in enumerable)
				items.Add(item);
			if (items.Count == 0)
				return engine.Intrinsics.Array.Construct(0);
			var arr = engine.Intrinsics.Array.Construct((uint)items.Count);
			for (var i = 0; i < items.Count; i++)
				arr[(uint)i] = ToValue(engine, items[i], context);
			return arr;
		}

		private static JsValue ToJsonObject(JintEngine engine, JObject jo, IJintScriptingContext context) {
			var obj = engine.Intrinsics.Object.Construct(Array.Empty<JsValue>(), engine.Intrinsics.Object);
			foreach (var prop in jo.Properties())
				obj.Set(prop.Name, ToValue(engine, prop.Value, context), true);
			return obj;
		}

		private static JsValue ToJsonArray(JintEngine engine, JArray ja, IJintScriptingContext context) {
			var arr = engine.Intrinsics.Array.Construct((uint)ja.Count);
			for (var i = 0; i < ja.Count; i++)
				arr[(uint)i] = ToValue(engine, ja[i], context);
			return arr;
		}

		private static JsValue ToStringKeyedObject(JintEngine engine, IDictionary<string, object> dict, IJintScriptingContext context) {
			var obj = engine.Intrinsics.Object.Construct(Array.Empty<JsValue>(), engine.Intrinsics.Object);
			foreach (var kv in dict)
				obj.Set(kv.Key, ToValue(engine, kv.Value, context), true);
			return obj;
		}

		public static JsValue ToValue(JintEngine engine, object value, IJintScriptingContext context = null)
			=> value switch {
				null                                                  => JsValue.Null,
				bool b                                                => b ? JsBoolean.True : JsBoolean.False,

				JsValue v                                             => v,
				Exception ex                                          => ToJsError(engine, ex),
				JObject jo                                            => ToJsonObject(engine, jo, context),
				JArray ja                                             => ToJsonArray(engine, ja, context),
				JValue jv                                             => ToValue(engine, jv.Value, context),
				JToken jt                                             => ToValue(engine, jt.ToString(), context),

				Task<object> { IsCompleted: true } t                  => (t.IsFaulted || t.IsCanceled) ? JsValue.Null : ToValue(engine, t.GetAwaiter().GetResult(), context),
				Task<object> t                                        => ToPromise(engine, t.AsUniTask(), context),
				UniTask<object> { Status: UniTaskStatus.Succeeded } t => ToValue(engine, t.GetAwaiter().GetResult(), context),
				UniTask<object> t                                     => ToPromise(engine, t, context),

				_ when context != null                                => ToValueViaContext(engine, value, context),
				_ when value.GetType().IsArray                        => ToArray(engine, (Array)value, context),
				_                                                     => Fallback(engine, value, context)
			};
		
		/// <summary>
		/// Converts a .NET exception into a JS Error instance. If the exception is a
		/// <see cref="JavaScriptException"/> (i.e. Jint re-throwing a script-side error we caught earlier),
		/// the original JS error value is returned unchanged instead of being re-wrapped.
		/// </summary>
		private static JsValue ToJsError(JintEngine engine, Exception ex) {
			if (ex is JavaScriptException jsEx)
				return jsEx.Error;

			var jsError = engine.Intrinsics.Error.Construct(ex.Message ?? string.Empty);
			jsError.Set("name", JsValue.FromObject(engine, ex.GetType().Name), true);
			jsError.Set("stack", JsValue.FromObject(engine, $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"), true);
			// Keep a handle on the original .NET exception so FromValue can hand back the exact same
			// instance instead of a reconstructed JsScriptException when this error round-trips.
			jsError.DefineOwnProperty("__exception", new PropertyDescriptor(JsValue.FromObject(engine, ex), writable: false, enumerable: false, configurable: false));
			return jsError;
		}

		private static JsValue Fallback(JintEngine engine, object value, IJintScriptingContext context = null) {
			NoxLogger.LogWarning($"{value} ({value.GetType().FullName}) is not compatible, this may cause incompatibilities.", tag: nameof(JintTypeAdapter));
			return JsValue.FromObject(engine, value);
		}

		private static JsValue ToValueViaContext(JintEngine engine, object value, IJintScriptingContext context) {
			var converted = context.ToScript(value);
			if (converted is JsValue jv) return jv;
			if (!ReferenceEquals(converted, value)) 
				return ToValue(engine, converted, null);

			var t = value.GetType();
			if (t.IsPrimitive || t == typeof(string) || t.IsEnum || t.IsValueType)
				return JsValue.FromObject(engine, value);

			if (value is IDictionary<string, object> stringDict)
				return ToStringKeyedObject(engine, stringDict, context);

			if (t.IsArray)
				return ToArray(engine, (Array)value, context);
			if (value is IEnumerable enumerable)
				return ToEnumerable(engine, enumerable, context);

			NoxLogger.LogWarning($"{value} ({value.GetType().FullName}) is not compatible, this may cause incompatibilities.", tag: nameof(JintTypeAdapter));
			if (value is UnityEngine.Object)
				return ObjectWrapper.Create(engine, value, value.GetType());
			return BuildReflective(engine, value, context);
		}

		private static ObjectInstance BuildReflective(JintEngine engine, object instance, IJintScriptingContext ctx) {
			var obj  = engine.Intrinsics.Object.Construct(Array.Empty<JsValue>(), engine.Intrinsics.Object);
			var type = instance.GetType();

			foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
				if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
					continue;
				var p    = prop;
				var jsName = new NameResolver(p.Name).Resolve(NameResolver.camelCaseStyle);
				var getter = new ClrFunction(engine, "get", (_, _2) => {
					try   { return ToValue(engine, p.GetValue(instance), ctx); }
					catch { return JsValue.Undefined; }
				});
				if (prop.CanWrite) {
					var setter = new ClrFunction(engine, "set", (_, args) => {
						try {
							var raw = args.Length > 0 ? FromValue(args[0]) : null;
							p.SetValue(instance, TryCoerceArg(raw, p.PropertyType));
						} catch { }
						return JsValue.Undefined;
					});
					obj.DefineOwnProperty(jsName, new GetSetPropertyDescriptor(getter, setter, enumerable: true, configurable: true));
				} else {
					obj.DefineOwnProperty(jsName, new GetSetPropertyDescriptor(getter, null, enumerable: true, configurable: false));
				}
			}

			var addedMethods = new HashSet<string>();
			foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
				if (method.IsSpecialName || method.IsGenericMethodDefinition)
					continue;
				var parameters = method.GetParameters();
				if (parameters.Any(p => p.IsOut || p.ParameterType.IsByRef))
					continue;
				if (method.DeclaringType == typeof(object) && method.Name != "ToString")
					continue;
				var jsMethodName = new NameResolver(method.Name).Resolve(NameResolver.camelCaseStyle);
				if (!addedMethods.Add(jsMethodName))
					continue;
				var m  = method;
				var ps = parameters;
				var fn = new ClrFunction(engine, jsMethodName, (_, args) => {
					try {
						var native    = ConvertArgs(args);
						var typedArgs = new object[ps.Length];
						for (var i = 0; i < ps.Length; i++) {
							var raw = i < native.Length ? native[i] : null;
							typedArgs[i] = raw == null && ps[i].HasDefaultValue
								? ps[i].DefaultValue
								: TryCoerceArg(raw, ps[i].ParameterType);
						}
						return ToValue(engine, m.Invoke(instance, typedArgs), ctx);
					} catch { return JsValue.Undefined; }
				});
				obj.DefineOwnProperty(jsMethodName, new PropertyDescriptor(fn, writable: true, enumerable: true, configurable: true));
			}

			return obj;
		}

		private static object TryCoerceArg(object value, Type target) {
			if (value == null) return target.IsValueType ? Activator.CreateInstance(target) : null;
			if (target.IsInstanceOfType(value)) return value;
			try { return Convert.ChangeType(value, target); } catch { return value; }
		}

		static internal JsValue ToPromise(JintEngine engine, UniTask<object> task, IJintScriptingContext context = null) {
			if (task.Status == UniTaskStatus.Succeeded)
				return ToValue(engine, task.GetAwaiter().GetResult(), context);

			var (promise, resolve, reject) = engine.Advanced.RegisterPromise();
			task.Then(
				onSuccess: v => {
					UniTask.Post(() => {
						engine.ResetTimeout();
						resolve(ToValue(engine, v, context));
						engine.Advanced.ProcessTasks();
					});
				},
				onError: ex => {
					UniTask.Post(() => {
						engine.ResetTimeout();
						NoxLogger.LogWarning($"Async method failed: {ex.Message}", tag: "jint_async_exception");
						reject(ToValue(engine, ex, context));
						engine.Advanced.ProcessTasks();
					});
				}
			).Forget();

			return promise;
		}

		internal static void ResetTimeout(this JintEngine engine) {
		    var fieldInfo = typeof(JintEngine).GetField("_constraints", BindingFlags.NonPublic | BindingFlags.Instance);
		    if (fieldInfo?.GetValue(engine) is not IEnumerable<Constraint> constraints)
		        return;

		    foreach (var constraint in constraints)
		        if (constraint != null && constraint.GetType().FullName == "Jint.Constraints.TimeConstraint") {
		            constraint.Reset();
		            break;
		        }
		}

		private static object[] ConvertArgs(JsValue[] args) {
			if (args.Length == 0)
				return Array.Empty<object>();
			var result = new object[args.Length];
			for (var i = 0; i < args.Length; i++)
				result[i] = FromValue(args[i]);
			return result;
		}


		public static object FromValue(JsValue value) => value switch {
		    _ when value.IsNull() || value.IsUndefined() => null,
		    _ when value.IsCallable()                    => new Func<object[], object>(args => {
		        var engine = value.AsObject().Engine;
		        var jsArgs = args?.Select(a => ToValue(engine, a)).ToArray() ?? Array.Empty<JsValue>();
		        return FromValue(value.Call(jsArgs));
		    }),
		    _ when !value.IsObject()                     => value.ToObject(),

		    ArrayInstance arr     => arr.ToArray().Select(FromValue).ToArray(),
		    ObjectWrapper wrapper => wrapper.Target,
		    JsError jsError       => FromJsError(jsError),

		    ObjectInstance obj when !obj.Get("__target").IsUndefined() && !obj.Get("__target").IsNull() 
		        => FromValue(obj.Get("__target")),

			// Intercepte les objets littéraux JS anonymes sans déclencher le warning de fallback
    		ObjectInstance obj => new PropertyDictionary(obj.Engine, obj),

		    _ => FallbackFromValue(value)
		};

		private static object FallbackFromValue(JsValue value) {
		    NoxLogger.LogWarning($"{value} ({value.GetType().FullName}) is not compatible, this may cause incompatibilities.", tag: nameof(JintTypeAdapter));
		    return new PropertyDictionary(value.AsObject().Engine, value.AsObject());
		}

		/// <summary>
		/// Converts a JS Error instance back into a .NET exception. If the error was produced by
		/// <see cref="ToJsError"/> from an original .NET exception, that exact instance is returned;
		/// otherwise a <see cref="JsScriptException"/> carrying the error's name/message/stack is built.
		/// </summary>
		private static Exception FromJsError(JsError jsError) {
			var wrapped = jsError.Get("__exception");
			if (wrapped?.IsUndefined() == false && !wrapped.IsNull() && FromValue(wrapped) is Exception original)
				return original;

			var name    = jsError.Get("name")?.ToString() ?? "Error";
			var message = jsError.Get("message")?.ToString() ?? string.Empty;
			var stack   = jsError.Get("stack")?.ToString();
			return new JsScriptException(name, message, stack);
		}
	}

	/// <summary>
	/// .NET representation of a JavaScript Error that did not originate from a wrapped .NET exception
	/// (e.g. a plain "throw new Error(...)" in script). Preserves the JS error's name, message and stack
	/// so it behaves like a normal exception on the C# side.
	/// </summary>
	public sealed class JsScriptException : Exception {
		public string JsName { get; }
		public string JsStack { get; }

		public JsScriptException(string jsName, string message, string jsStack) : base(message) {
			JsName  = jsName;
			JsStack = jsStack;
		}

		public override string ToString() => string.IsNullOrEmpty(JsStack) ? $"{JsName}: {Message}" : $"{JsName}: {Message}\n{JsStack}";
	}
}