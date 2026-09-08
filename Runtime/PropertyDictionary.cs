using System;
using System.Collections;
using System.Collections.Generic;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using JintEngine = Jint.Engine;

namespace Nox.Jint.Runtime {
	/// <summary>
	/// A live <c>IDictionary&lt;string, object&gt;</c> view over a Jint
	/// <see cref="ObjectInstance"/>. Reads and writes are forwarded to the
	/// underlying JS object in place — nothing is materialized up-front, so
	/// there is no recursion and no stale copy.
	///
	/// <para>
	/// Returned by <see cref="JintTypeAdapter.FromJsValue"/> for plain JS
	/// objects, so consuming modules only need standard
	/// <see cref="IDictionary{TKey,TValue}"/> semantics
	/// (<c>TryGetValue</c>, <c>ContainsKey</c>, <c>this[key]</c>, enumeration)
	/// with no Jint-specific duck-typing.
	/// </para>
	/// </summary>
	public sealed class PropertyDictionary : IDictionary<string, object> {
		private readonly JintEngine       _engine;
		private readonly ObjectInstance   _target;

		public PropertyDictionary(JintEngine engine, ObjectInstance target) {
			_engine = engine;
			_target = target;
		}

		/// <summary>The underlying JS object (for callers that need the raw instance).</summary>
		public ObjectInstance Target 
            => _target;

		// ── Reads (forwarded live) ────────────────────────────────────────

		public object this[string key] {
			get {
				var value = _target.Get(key);
				return value.IsUndefined() ? null : JintTypeAdapter.FromJsValue(value);
			}
			set => Set(key, value);
		}

		public bool TryGetValue(string key, out object value) {
			if (!_target.HasProperty(key)) {
				value = null;
				return false;
			}
			var raw = _target.Get(key);
			value = raw.IsUndefined() ? null : JintTypeAdapter.FromJsValue(raw);
			return true;
		}

		public bool ContainsKey(string key)
			=> _target.HasProperty(key);

		public int Count {
			get {
				var n = 0;
				foreach (var kv in _target.GetOwnProperties())
					if (kv.Value.Enumerable) n++;
				return n;
			}
		}

		public ICollection<string> Keys {
			get {
				var keys = new List<string>();
				foreach (var kv in _target.GetOwnProperties())
					if (kv.Value.Enumerable)
						keys.Add(kv.Key.ToString());
				return keys;
			}
		}

		public ICollection<object> Values 
            => new ValueCollection(this);

		public bool IsReadOnly 
            => false;

		// ── Writes (forwarded live) ───────────────────────────────────────

		public void Add(string key, object value) {
			if (ContainsKey(key))
				throw new ArgumentException($"Key already exists: {key}");
			Set(key, value);
		}

		public void Add(KeyValuePair<string, object> item)
			=> Add(item.Key, item.Value);

		public bool Remove(string key)
			=> _target.Delete(key);

		public bool Remove(KeyValuePair<string, object> item) {
			if (!TryGetValue(item.Key, out var current)
				|| !Equals(current, item.Value))
				return false;
			return Remove(item.Key);
		}

		public void Clear() {
			foreach (var key in Keys)
				_target.Delete(key);
		}

		public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) {
			if (array == null) throw new ArgumentNullException(nameof(array));
			if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
			foreach (var kv in this) {
				if (arrayIndex >= array.Length) throw new ArgumentException("Destination array is too small.");
				array[arrayIndex++] = kv;
			}
		}

		public bool Contains(KeyValuePair<string, object> item)
			=> TryGetValue(item.Key, out var current) && Equals(current, item.Value);

		public IEnumerator<KeyValuePair<string, object>> GetEnumerator() {
			foreach (var kv in _target.GetOwnProperties()) {
				if (kv.Value.Enumerable == false) continue;
				var jsKey = kv.Key;
				var jsVal = kv.Value.IsDataDescriptor() ? kv.Value.Value : _target.Get(jsKey);
				yield return new KeyValuePair<string, object>(
					jsKey.ToString(),
					jsVal.IsUndefined() ? null : JintTypeAdapter.FromJsValue(jsVal)
				);
			}
		}

		IEnumerator IEnumerable.GetEnumerator() 
            => GetEnumerator();

		// ── Internal ──────────────────────────────────────────────────────

		private void Set(string key, object value) {
			var jsKey = JsValue.FromObject(_engine, key);
			var jsVal = JintTypeAdapter.ToValue(_engine, value);
			_target.Set(jsKey, jsVal, throwOnError: true);
		}

		private sealed class ValueCollection : ICollection<object> {
			private readonly PropertyDictionary _owner;
			public ValueCollection(PropertyDictionary owner) 
                => _owner = owner;

			public int  Count      
                => _owner.Count;

			public bool IsReadOnly 
                => true;

			public void Add(object item)                  
                => throw new NotSupportedException();

			public void Clear()                            
                => throw new NotSupportedException();

			public bool Remove(object item)                
                => throw new NotSupportedException();

			public bool Contains(object item) {
				foreach (var kv in _owner)
					if (Equals(kv.Value, item)) return true;
				return false;
			}

			public void CopyTo(object[] array, int arrayIndex) {
				if (array == null) throw new ArgumentNullException(nameof(array));
				if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
				foreach (var kv in _owner) {
					if (arrayIndex >= array.Length) throw new ArgumentException("Destination array is too small.");
					array[arrayIndex++] = kv.Value;
				}
			}

			public IEnumerator<object> GetEnumerator() {
				foreach (var kv in _owner)
					yield return kv.Value;
			}

			IEnumerator IEnumerable.GetEnumerator() 
                => GetEnumerator();
		}
	}
}