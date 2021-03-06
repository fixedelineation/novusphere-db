﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.IO;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;

namespace Novusphere.Database
{
    using JsonWriterSettings = MongoDB.Bson.IO.JsonWriterSettings;
    using JsonOutputMode = MongoDB.Bson.IO.JsonOutputMode;

    public class HttpContextHandler
    {
        public HttpListenerRequest Request { get; private set; }
        public HttpListenerResponse Response { get; private set; }
        public QuerySession Session { get; private set;}

        public HttpContextHandler(HttpListenerContext context, QuerySession session)
        {
            Request = context.Request;
            Response = context.Response;
            Session = session;
        }

        public void Handle()
        {
            var path = Request.Url.AbsolutePath.ToLower();
            switch (path)
            {
                case "/api": HandleAPI(); break;
                case "/hello": HandleHello(); break;
                case "/plugins": HandlePlugins(); break;
                default:
                    {
                        bool match = false;
                        var content = GetPostContent();

                        foreach (var p in Program.PluginManager.Plugins)
                        {
                            var result = p.HandleHttp(Session.GetDatabase(), path, content, out match);
                            if (match)
                                Json(result);
                        }

                        if (!match)
                            Content("Unknown path request", "text/plain");
                        break;
                    }
            }
        }

        private string GetPostContent()
        {
            string content;
            using (var rdr = new StreamReader(Request.InputStream))
                content = rdr.ReadToEnd();
            return content;
        }

        private HttpListenerResponse Content(string content, string type)
        {
            Response.AddHeader("Access-Control-Allow-Origin", "*");
            Response.StatusCode = 200; // OK
            Response.ContentType = type;

            using (var wrtr = new StreamWriter(Response.OutputStream))
                wrtr.Write(content);
            return Response;
        }

        private HttpListenerResponse Json(object obj)
        {
            return Content(JsonConvert.SerializeObject(obj), "application/json");
        }

        private HttpListenerResponse HandlePlugins()
        {
            return Json(Program.PluginManager.Plugins.Select(p => p.GetType().FullName));
        }

        private HttpListenerResponse HandleHello()
        {
            return Content("hello", "text/plain");
        }

        private HttpListenerResponse HandleAPI()
        {
            try
            {
                var result = Session.RunQuery(GetPostContent());
                return Content(result.ToJson(new JsonWriterSettings() { OutputMode = JsonOutputMode.Strict }), "application/json");
            }
            catch (MongoCommandException ex)
            {
                return Json(new
                {
                    error = new
                    {
                        allowedTimeMS = (int)Session.AllowedTimeMS,
                        type = ex.GetType().Name,
                        message = ex.Result["errmsg"],
                        code = ex.Result["code"],
                        codeName = ex.Result["codeName"],
                        command = ex.Command.ToString()
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = new
                    {
                        allowedTimeMS = (int)Session.AllowedTimeMS,
                        type = ex.GetType().Name,
                        message = ex.Message
                    }
                });
            }
        }
    }
}
