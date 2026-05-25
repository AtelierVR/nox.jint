using Nox.Scripting;
using JintEngine = Jint.Engine;

namespace Nox.Jint {
	/// <summary>
	/// Extends <see cref="IScriptingContext"/> with direct access to the underlying
	/// Jint <see cref="JintEngine"/> instance. Implemented by the Jint backing classes.
	/// </summary>
	public interface IJintScriptingContext : IScriptingContext {
		/// <summary>The Jint engine executing the current script.</summary>
		JintEngine Engine { get; }
	}
}
