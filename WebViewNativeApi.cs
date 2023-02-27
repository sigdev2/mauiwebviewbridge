using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebViewNativeApi
{
    public class NativeBridge
    {
        private const string DEFAULT_SCHEME = "native://";
        private const string INTERFACE_JS = "window['createNativeBridgeProxy'] = " +
            "(name, methods, properties, scheme = '" + DEFAULT_SCHEME  + "') =>" +
            "{" +
            "    let apiCalls = new Map();" +
            "" +
            "    function createRequest(target, success, reject, argumentsList) {" +
            "        let uuid = crypto.randomUUID();" +
            "        while(apiCalls.has(uuid)) { uuid = crypto.randomUUID(); };" +
            "        apiCalls.set(uuid, { 'success': success, 'reject': reject, 'arguments': argumentsList });" +
            "        location.href = scheme + name + '/' + target + '/' + uuid + '/';" +
            "    }" +
            "" +
            "    return new Proxy({" +
            "            getArguments : (token) => {" +
            "                return apiCalls.get(token).arguments;" +
            "            }," +
            "            returnValue : (token, value) => {" +
            "                let ret = value;" +
            "                try { ret = JSON.parse(ret); } catch(e) { };" +
            "                let callback = apiCalls.get(token).success;" +
            "                if (callback && typeof callback === 'function')" +
            "                    callback(ret);" +
            "                apiCalls.delete(token);" +
            "            }," +
            "            rejectCall : (token) => {" +
            "                let callback = apiCalls.get(token).reject;" +
            "                if (callback && typeof callback === 'function')" +
            "                    callback();" +
            "                apiCalls.delete(token);" +
            "            }" +
            "        }," +
            "        {" +
            "            get: (target, prop, receiver) => {" +
            "                if (methods.includes(prop)) {" +
            "                    return new Proxy(() => {}, {" +
            "                        apply: (target, thisArg, argumentsList) => {" +
            "                            return new Promise((success, reject) => {" +
            "                                    createRequest(prop, success, reject, argumentsList);" +
            "                                });" +
            "                        }" +
            "                    });" +
            "                }" +
            "                if (!properties.includes(prop)) {" +
            "                    return Reflect.get(target, prop, receiver);" +
            "                }" +
            "                return new Promise((success, reject) => {" +
            "                        createRequest(prop, success, reject, []);" +
            "                    });" +
            "            }," +
            "            set: (target, prop, value) => {" +
            "                return new Promise((success, reject) => {" +
            "                        createRequest(prop, success, reject, [value]);" +
            "                    });" +
            "            }" +
            "        });" +
            "};";

        private readonly WebView _webView = null;
        private readonly Dictionary<(string, string), Object> _targets = new();
        private bool _isInit = false;
        private (string, string, string, Object) _query = ("", "", "", null);

        public NativeBridge(WebView wv)
        {
            _webView = wv;
            _webView.Navigated += OnWebViewInit;
            _webView.Navigating += OnWebViewNavigatin;
        }

        public void AddTarget(string name, Object obj, string sheme = DEFAULT_SCHEME)
        {
            _targets.Add((name, sheme), obj);

            if (_isInit)
            {
                AddTargetToWebView(name, obj, sheme);
            }
        }

        private void OnWebViewInit(object sender, WebNavigatedEventArgs e)
        {
            if (!_isInit)
            {
                RunJS(INTERFACE_JS);
                foreach (KeyValuePair<(string, string), Object> entry in _targets)
                {
                    AddTargetToWebView(entry.Key.Item1, entry.Value, entry.Key.Item2);
                }
                _isInit = true;
            } else if (_query.Item4 != null)
            {
                Task.Run(() =>
                {
                    RunCommand(_query.Item1, _query.Item2, _query.Item3, _query.Item4);
                    _query = ("", "", "", null);
                });
            }
        }

        private void OnWebViewNavigatin(object sender, WebNavigatingEventArgs e)
        {
            if (_isInit)
            {
                foreach (KeyValuePair<(string, string), Object> entry in _targets)
                {
                    string startStr = entry.Key.Item2 + entry.Key.Item1;
                    if (e.Url.StartsWith(startStr))
                    {
                        string request = e.Url[(e.Url.IndexOf(startStr) + startStr.Length)..].ToLower();
                        request = request.Trim(new Char[] { '/', '\\' });
                        string[] requestArgs = request.Split('/');
                        if (request.Length < 2)
                            return;

                        string prop = requestArgs[0];
                        string token = requestArgs[1];

                        Type type = entry.Value.GetType();
                        if (type.GetMember(prop) == null)
                            return;
                        e.Cancel = true;

                        _query = (entry.Key.Item1, token, prop, entry.Value);
                        return;
                    }
                }
            }
        }

        private void AddTargetToWebView(string name, Object obj, string sheme)
        {
            Type type = obj.GetType();
            List<string> methods = new List<string>();
            List<string> properties = new List<string>();
            foreach (MethodInfo method in type.GetMethods())
            {
                methods.Add(method.Name);
            }
            foreach (PropertyInfo p in type.GetProperties())
            {
                properties.Add(p.Name);
            }

            RunJS("window." + name + " = window.createNativeBridgeProxy('" + name + "', " + JsonSerializer.Serialize(methods) + ", " +
                JsonSerializer.Serialize(properties) + ", '" + sheme + "');");
        }

        private static bool IsAsyncMethod(MethodInfo method)
        {
            Type attType = typeof(AsyncStateMachineAttribute);
            var attrib = (AsyncStateMachineAttribute)method.GetCustomAttribute(attType);

            return (attrib != null);
        }

        private async void RunCommand(string name, string token, string prop, Object obj)
        {
            try
            {
                Type type = obj.GetType();
                string readArguments = await RunJS("window." + name + ".getArguments('" + token + "');");
                JsonElement[] jsonObjects = JsonSerializer.Deserialize<JsonElement[]>(Regex.Unescape(readArguments));
                MethodInfo method = type.GetMethod(prop);
                if (method != null)
                {
                    Object[] arguments = new Object[jsonObjects.Length];
                    foreach (ParameterInfo arg in method.GetParameters())
                    {
                        JsonElement jsonObject = jsonObjects[arg.Position];
                        arguments[arg.Position] = jsonObject.Deserialize(arg.ParameterType);
                    }

                    Object result = method.Invoke(obj, arguments);
                    string serializedRet = "null";
                    if (result != null)
                    {
                        if (IsAsyncMethod(method))
                        {
                            Task task = (Task)result;
                            await task.ConfigureAwait(false);
                            result = (object)((dynamic)task).Result;
                        }
                        serializedRet = JsonSerializer.Serialize(result);
                    }
                    
                    await RunJS("window." + name + ".returnValue('" + token + "', " + serializedRet  + ");");
                }
                else
                {
                    PropertyInfo propety = type.GetProperty(prop);
                    if (propety != null)
                    {
                        if (jsonObjects != null && jsonObjects.Length > 0)
                            propety.SetValue(obj, jsonObjects[0].Deserialize(propety.PropertyType));
                        string result = JsonSerializer.Serialize(propety.GetValue(obj, null));
                        await RunJS("window." + name + ".returnValue('" + token + "', " + result + ");");
                    } else
                    {
                        await RunJS("window." + name + ".rejectCall('" + token + "');");
                    }
                }
            } catch
            {
                await RunJS("console.error('Internal error!'); window." + name + ".rejectCall('" + token + "');");
            }
        }

        public Task<string> RunJS(string code)
        {
            return _webView.Dispatcher.DispatchAsync(() =>
            {
                string resultCode = code;
                if (resultCode.Contains("\\n") || resultCode.Contains('\n'))
                {
                    resultCode = "console.error('Called js from native api contain new line symbols!')";
                }
                else
                {
                    resultCode = "try { " + resultCode + " } catch(e) { console.error(e); }";
                }

                return _webView.EvaluateJavaScriptAsync(resultCode);
            });
        }
    }
}
