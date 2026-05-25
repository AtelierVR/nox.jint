using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Nox.CCK.Scripting;
using Nox.Jint;
using Nox.Scripting;
using JintEngine = Jint.Engine;
using NoxLogger = Nox.CCK.Utils.Logger;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Jint.Native.Array;
using Jint.Runtime.Modules;
using Nox.CCK.Utils;

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
			try {
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
									property.Setter(ctx, instance, FromJsValue(args[0]));
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
									NoxLogger.LogError($"[scripting] {converter.HandledType.Name}.{name}: {e.Message}");
									return JsValue.Null;
								}
							});
							obj.DefineOwnProperty(name, new PropertyDescriptor(fn, writable: true, enumerable: true, configurable: true));
							break;
						}
						case IScriptingTypeAsyncMethod method: {
							var fn = new ClrFunction(engine, name, (_, args) => {
								var          nativeArgs = ConvertArgs(args);
								Task<object> task;
								try { task = method.Handler(ctx, instance, nativeArgs); } catch (Exception e) {
									NoxLogger.LogError($"[scripting] {converter.HandledType.Name}.{name}: {e.Message}");
									return JsValue.Null;
								}
								return ToValue(engine, task, ctx);
							});
							obj.DefineOwnProperty(name, new PropertyDescriptor(fn, writable: true, enumerable: true, configurable: true));
							break;
						}
					}
				}
			} catch (Exception e) {
				NoxLogger.LogError($"[scripting] BuildObject({converter.HandledType.Name}): {e.Message}");
			}
			return obj;
		}

		public static void BindModule(JintEngine engine, ModuleBuilder builder, IScriptingModuleDefinition module, IJintScriptingContext ctx) {
			try {
				// Namespace object for `import Mod from 'module'` (default import)
				var ns = engine.Intrinsics.Object.Construct(Array.Empty<JsValue>(), engine.Intrinsics.Object);

				foreach (var binding in module.Bindings) {
					switch (binding) {
						case IScriptingPropertyDefinition property: {
							var name = binding.Name.Resolve(NameResolver.camelCaseStyle);
							// Named export: evaluated once at module init (required by ModuleBuilder API)
							builder.ExportValue(name, ToValue(engine, property.Getter(ctx), ctx));
							// Namespace: live getter, and setter if the property is writable
							var nsGetter = new ClrFunction(engine, "get", (_, _) => ToValue(engine, property.Getter(ctx), ctx));
							ClrFunction nsSetter = null;
							if (property.Setter != null)
								nsSetter = new ClrFunction(engine, "set", (_, args) => {
									property.Setter(ctx, FromJsValue(args.Length > 0 ? args[0] : JsValue.Undefined));
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
									NoxLogger.LogError($"[scripting] {module.Id.Resolve(NameResolver.snake_case_style)}.{name}: {e.Message}");
									return JsValue.Null;
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
								Task<object> task;
								try { task = method.Handler(ctx, nativeArgs); } catch (Exception e) {
									NoxLogger.LogError($"[scripting] {module.Id.Resolve(NameResolver.snake_case_style)}.{name}: {e.Message}");
									return JsValue.Null;
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

				// Default export = namespace object, enables: import Mod from 'module'; Mod.foo()
				builder.ExportValue("default", ns);
			} catch (Exception e) {
				NoxLogger.LogError($"[scripting] BuildModule({module.Id.Resolve(NameResolver.snake_case_style)}): {e.Message}");
			}
		}

		public static ObjectInstance BuildType(JintEngine engine, IScriptingTypeConverter converter, IJintScriptingContext ctx) {
			var obj = engine.Intrinsics.Object.Construct(Array.Empty<JsValue>(), engine.Intrinsics.Object);
			obj.DefineOwnProperty("Name", new PropertyDescriptor(converter.HandledType.Name, writable: false, enumerable: false, configurable: false));
			try {
				if (converter.Constructor != null) {
					var constructorFn = new ClrFunction(engine, converter.HandledType.Name, (_, args) => {
						try { return ToValue(engine, converter.Constructor(ctx, ConvertArgs(args)), ctx); } catch (Exception e) {
							NoxLogger.LogError($"[scripting] {converter.HandledType.Name} constructor: {e.Message}");
							return JsValue.Null;
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
									property.Setter(ctx, null, FromJsValue(args[0]));
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
									NoxLogger.LogError($"[scripting] {converter.HandledType.Name}.{name}: {e.Message}");
									return JsValue.Null;
								}
							});
							obj.DefineOwnProperty(name, new PropertyDescriptor(fn, writable: true, enumerable: true, configurable: true));
							break;
						}
						case IScriptingTypeAsyncMethod method: {
							var asyncFn = new ClrFunction(engine, name, (_, args) => {
								var          nativeArgs = ConvertArgs(args);
								Task<object> task;
								try { task = method.Handler(ctx, null, nativeArgs); } catch (Exception e) {
									NoxLogger.LogError($"[scripting] {converter.HandledType.Name}.{name}: {e.Message}");
									return JsValue.Null;
								}
								return ToValue(engine, task, ctx);
							});
							obj.DefineOwnProperty(name, new PropertyDescriptor(asyncFn, writable: true, enumerable: true, configurable: true));
							break;
						}
					}
				}
			} catch (Exception e) {
				NoxLogger.LogError($"[scripting] BuildType({converter.HandledType.Name}): {e.Message}");
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

		public static JsValue ToValue(JintEngine engine, object value, IJintScriptingContext context = null)
			=> value switch {
				JsValue v                                             => v,
				bool b                                                => b ? JsBoolean.True : JsBoolean.False,
				null                                                  => JsValue.Null,
				Task<object> { IsCompleted: true } t                  => (t.IsFaulted || t.IsCanceled) ? JsValue.Null : ToValue(engine, t.GetAwaiter().GetResult(), context),
				Task<object> t                                        => ToPromise(engine, t.AsUniTask(), context),
				UniTask<object> { Status: UniTaskStatus.Succeeded } t => ToValue(engine, t.GetAwaiter().GetResult(), context),
				UniTask<object> t                                     => ToPromise(engine, t, context),
				_ when value.GetType().IsArray                        => ToArray(engine, (Array)value, context),
				_ when context != null                                => ToValueViaContext(engine, value, context),
				_                                                     => JsValue.FromObject(engine, value)
			};

		private static JsValue ToValueViaContext(JintEngine engine, object value, IJintScriptingContext context) {
			var converted = context.ToScript(value);
			if (converted is JsValue jv) return jv;
			if (!ReferenceEquals(converted, value)) return ToValue(engine, converted, null);
			// No converter registered — use primitive fast-path or reflective wrapper.
			var t = value.GetType();
			if (t.IsPrimitive || t == typeof(string) || t.IsEnum || t.IsValueType)
				return JsValue.FromObject(engine, value);
			// Unity Objects without a registered converter: wrap via ObjectWrapper so the
			// script can still read/write native properties (e.g. TMP_Text.text, Image.color).
			if (value is UnityEngine.Object)
				return ObjectWrapper.Create(engine, value, value.GetType());
			return BuildReflective(engine, value, context);
		}

		/// <summary>
		/// Builds a Jint <see cref="ObjectInstance"/> from any reference-type instance
		/// by reflecting its public properties and methods.
		/// Return values are recursively converted via <see cref="ToValue"/>.
		/// </summary>
		private static ObjectInstance BuildReflective(JintEngine engine, object instance, IJintScriptingContext ctx) {
			var obj  = engine.Intrinsics.Object.Construct(Array.Empty<JsValue>(), engine.Intrinsics.Object);
			var type = instance.GetType();

			// ── Properties ────────────────────────────────────────────────
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
							var raw = args.Length > 0 ? FromJsValue(args[0]) : null;
							p.SetValue(instance, TryCoerceArg(raw, p.PropertyType));
						} catch { }
						return JsValue.Undefined;
					});
					obj.DefineOwnProperty(jsName, new GetSetPropertyDescriptor(getter, setter, enumerable: true, configurable: true));
				} else {
					obj.DefineOwnProperty(jsName, new GetSetPropertyDescriptor(getter, null, enumerable: true, configurable: false));
				}
			}

			// ── Methods ───────────────────────────────────────────────────
			var addedMethods = new HashSet<string>();
			foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
				// Skip property accessors, generic definitions, by-ref params
				if (method.IsSpecialName || method.IsGenericMethodDefinition)
					continue;
				var parameters = method.GetParameters();
				if (parameters.Any(p => p.IsOut || p.ParameterType.IsByRef))
					continue;
				// Skip boring object methods except ToString
				if (method.DeclaringType == typeof(object) && method.Name != "ToString")
					continue;
				var jsMethodName = new NameResolver(method.Name).Resolve(NameResolver.camelCaseStyle);
				// First overload wins
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

		/// <summary>Coerce a raw JS-extracted value to the expected .NET parameter/property type.</summary>
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
					resolve(ToValue(engine, v, context));
					engine.Advanced.ProcessTasks();
				},
				onError: ex => {
					NoxLogger.LogError($"Async method failed: {ex.Message}", tag: "jint_async_exception");
					reject(JsValue.FromObject(engine, ex.Message));
					engine.Advanced.ProcessTasks();
				}
			).Forget();

			return promise;
		}

		/// <summary>Converts a Jint argument array to native objects without LINQ allocations.</summary>
		private static object[] ConvertArgs(JsValue[] args) {
			if (args.Length == 0)
				return Array.Empty<object>();
			var result = new object[ args.Length ];
			for (var i = 0; i < args.Length; i++)
				result[i] = FromJsValue(args[i]);
			return result;
		}

		/// <summary>Convert a <see cref="JsValue"/> to a plain C# object for handler arguments.</summary>
		public static object FromJsValue(JsValue value) {
			if (value.IsNull() || value.IsUndefined())
				return null;
			if (!value.IsObject())
				return value.ToObject();
			var obj = value.AsObject();
			if (obj is ArrayInstance arr) {
				var items  = arr.ToArray();
				var result = new object[ items.Length ];
				for (var i = 0; i < items.Length; i++)
					result[i] = FromJsValue(items[i]);
				return result;
			}
			if (obj is ObjectWrapper wrapper)
				return wrapper.Target;
			return obj;
		}
	}
}