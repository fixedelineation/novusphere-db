using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;

namespace Novusphere.Database
{
    public class HttpServer
    {
        private HttpListener _listener;
        private ConcurrentDictionary<string, QuerySession> _sessions;

        public HttpServer()
        {
            _sessions = new ConcurrentDictionary<string, QuerySession>();
            _listener = new HttpListener();
            foreach (var uri in Program.Config.UriPrefixes)
                _listener.Prefixes.Add(uri);
        }

        private QuerySession GetSession(HttpListenerContext context) 
        {
            QuerySession qs;
            var id = context.Request.RemoteEndPoint.Address.ToString();
            if (!_sessions.TryGetValue(id, out qs))
            {
                qs = new QuerySession(id);
                _sessions[id] = qs;
            }
            return qs;
        }

        public void Start()
        {
            _listener.Start();
            _listener.BeginGetContext(GetContext, null);
        }

        public void GetContext(IAsyncResult result)
        {
            try
            {
                var context = _listener.EndGetContext(result);

                if (_listener.IsListening)
                    _listener.BeginGetContext(GetContext, null);

                var handler = new HttpContextHandler(context, GetSession(context));
                handler.Handle();
            }
            catch
            {
                // ...
            }
        }

        public void Stop()
        {
            _listener.Stop();
        }
    }
}
