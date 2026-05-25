using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace api.nox.jint {
	public class Logger {
		public readonly List<Log>  Logs = new();
		public          UnityEvent OnLog;

		public void Log(LogType logType, string message) {
			Logs.Add(
				new Log {
					Type    = logType,
					Message = message,
					Time    = DateTime.Now
				}
			);
			if (Logs.Count > 100)
				Logs.RemoveRange(0, Logs.Count - 100);
		}
	}

	public class Log {
		public LogType  Type;
		public string   Message;
		public DateTime Time;
	}
}