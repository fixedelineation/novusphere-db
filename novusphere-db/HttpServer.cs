using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace Novusphere.Database
{
    public class HttpServer
    {
        private HttpListener _listener;

        public HttpServer()
        {
            _listener = new HttpListener();
            foreach (var uri in Program.Config.UriPrefixes)
                _listener.Prefixes.Add(uri);
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
                var handler = new HttpContextHandler(context);
                handler.Handle();
            }
            catch
            {
                // ...
            }

            if (_listener.IsListening)
                _listener.BeginGetContext(GetContext, null);
        }

        public void Stop()
        {
            _listener.Stop();
        }
    }
}
