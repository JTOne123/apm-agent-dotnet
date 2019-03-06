﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Elastic.Apm.Logging;
using Elastic.Apm.Model.Payload;
using StackFrame = Elastic.Apm.Model.Payload.StackFrame;

namespace Elastic.Apm.Helpers
{
	internal static class StacktraceHelper
	{
		/// <summary>
		/// Turns a System.Diagnostic.StackFrame[] into a <see cref="Model.Payload.StackFrame" /> list which can be reported to the APM Server
		/// </summary>
		/// <param name="frames">The stack frames to rewrite into APM stack traces</param>
		/// <param name="logger">The logger to emit exceptions on should one occur</param>
		/// <param name="capturingFor">Just for logging.</param>
		/// <returns>A prepared List that can be passed to the APM server</returns>
		internal static List<StackFrame> GenerateApmStackTrace(System.Diagnostics.StackFrame[] frames, IApmLogger logger, string capturingFor)
		{
			var retVal = new List<StackFrame>(frames.Length);

			try
			{
				retVal.AddRange(from item in frames
					let fileName = item?.GetMethod()?.DeclaringType?.Assembly?.GetName()?.Name
					where !string.IsNullOrEmpty(fileName)
					select new StackFrame
					{
						Function = item?.GetMethod()?.Name,
						FileName = fileName,
						Module = item?.GetMethod()?.ReflectedType?.Name,
						LineNo = item?.GetFileLineNumber() ?? 0
					});
			}
			catch (Exception e)
			{
				logger?.LogWarning(e, "Failed capturing stacktrace for {ApmContext}", capturingFor);
				logger?.LogDebug(e, "Exception {ExceptionName}: {ExceptionMessage}", e.GetType().Name, e.Message);
			}

			return retVal;
		}
	}
}
