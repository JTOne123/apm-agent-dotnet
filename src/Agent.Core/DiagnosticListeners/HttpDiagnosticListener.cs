﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Elastic.Agent.Core.DiagnosticSource;
using Elastic.Agent.Core.Model.Payload;


namespace Elastic.Agent.Core.DiagnosticListeners
{
    public class HttpDiagnosticListener : IDiagnosticListener
    {
        public string Name => "HttpHandlerDiagnosticListener";

        private Config _agentConfig;

        public HttpDiagnosticListener(Config config)
        {
            _agentConfig = config;
        }

        /// <summary>
        /// Keeps track of ongoing requests
        /// </summary>
        private readonly ConcurrentDictionary<HttpRequestMessage, Span> _processingRequests = new ConcurrentDictionary<HttpRequestMessage, Span>();

        public void OnCompleted()
        {
            throw new NotImplementedException();
        }

        public void OnError(Exception error)
        {
            throw new NotImplementedException();
        }

        public void OnNext(KeyValuePair<string, object> kv)
        {
            var request = kv.Value.GetType().GetTypeInfo().GetDeclaredProperty("Request")?.GetValue(kv.Value) as HttpRequestMessage;

            if (IsRequestFiltered(request?.RequestUri))
            {
                return;
            }

            switch (kv.Key)
            {
                case "System.Net.Http.HttpRequestOut.Start": //TODO: look for consts
                    if (request != null)
                    {
                        if(TransactionContainer.Transactions == null || TransactionContainer.Transactions.Value == null)
                        {
                            return;
                        }

                        var transactionStartTime = TransactionContainer.Transactions.Value[0].TimestampInDateTime;
                        var utcNow = DateTime.UtcNow;

                        var http = new Http
                        {
                            Url = request.RequestUri.ToString(),
                            Method = request.Method.Method,
                        };

                        var span = new Span
                        {
                            Start = (decimal)(utcNow - transactionStartTime).TotalMilliseconds,
                            Name = $"{request.Method} {request.RequestUri.ToString()}",
                            Type = "Http",
                            Context = new Span.ContextC
                            {
                                Http = http
                            }
                        };

                        if (_processingRequests.TryAdd(request, span))
                        {
                            var frames = new System.Diagnostics.StackTrace().GetFrames();
                            var stackFrames = new List<Stacktrace>(); //TODO: use known size

                            try
                            {
                                foreach (var item in frames)
                                {
                                    var fileName = item?.GetMethod()?.DeclaringType?.Assembly?.GetName()?.Name;
                                    if (String.IsNullOrEmpty(fileName))
                                    {
                                        continue; //since filename is required by the server, if we don't have it we skip the frame
                                    }

                                    stackFrames.Add(new Stacktrace
                                    {
                                        Function = item?.GetMethod()?.Name,
                                        Filename = fileName,
                                        Module = item?.GetMethod()?.ReflectedType?.Name
                                    });
                                }
                            }
                            catch
                            {
                                //TODO: log
                            }

                            span.Stacktrace = stackFrames;
                        }
                    }
                    break;

                case "System.Net.Http.HttpRequestOut.Stop":
                    var response = kv.Value.GetType().GetTypeInfo().GetDeclaredProperty("Response").GetValue(kv.Value) as HttpResponseMessage;
                    var requestTaskStatusObj = (TaskStatus)kv.Value.GetType().GetTypeInfo().GetDeclaredProperty("RequestTaskStatus").GetValue(kv.Value);
                    var requestTaskStatus = (TaskStatus)requestTaskStatusObj;

                    if (_processingRequests.TryRemove(request, out Span mspan))
                    {
                        //TODO: response can be null if for example the request Task is Faulted. 
                        //E.g. writing this from an airplane without internet, and requestTaskStatus is "Faulted" and response is null
                        //How do we report this? There is no response code in that case.
                        if (response != null)
                        {
                            mspan.Context.Http.Status_code = (int)response.StatusCode;
                        }

                        //TODO: there are better ways
                        var transactionStartTime = TransactionContainer.Transactions.Value[0].TimestampInDateTime;
                        var endTime = (DateTime.UtcNow - transactionStartTime).TotalMilliseconds;
                        mspan.Duration = endTime - (double)mspan.Start;
                    }
                    else
                    {
                        //todo: log
                    }

                    TransactionContainer.Transactions?.Value[0]?.Spans?.Add(mspan);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Tells if the given request should be filtered from being captured. 
        /// </summary>
        /// <returns><c>true</c>, if request should not be captured, <c>false</c> otherwise.</returns>
        /// <param name="requestUri">Request URI. Can be null, which is not filtered</param>
        private bool IsRequestFiltered(Uri requestUri)
        {
            if (requestUri == null)
            {
                return false;
            }

            return _agentConfig.ServerUri.IsBaseOf(requestUri) ? true : false;
        }
    }
}