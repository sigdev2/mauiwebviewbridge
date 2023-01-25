# MAUI WebView bridge from C# to JS

One .cs file for add access to CSharp native objects in MAUI WebView JS scripts for create hybrid single-page applications (SPA) without using Blazor and any server side solutions.

## Mechanism

For interaction, the mechanism of interrupting navigation at a certain address is used. In JS the transition is called by changing location.href, and in C# it is intercepted in WebView::Navigating handler.

## Usage

It's simple!

Add WebView in your xaml:

    ...
    <WebView x:Name="webView" Source="Html/index.html">
    </WebView>
    ...

In xaml.cs add using for WebViewNativeApi and create in page constructor NativeBridge:

    ...
    using WebViewNativeApi;
    ...
    public MainPage()
    {
	InitializeComponent();

        WebView wvBrowser = FindByName("webView") as WebView;
        api = new NativeBridge(wvBrowser);
    }
    ...
    
Add some CSharp calss object to JS using NativeBridge:

    ...
    class NativeApi : Object
    {
        public async Task<string> open_file_dialog()
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions());
            using var stream = await result.OpenReadAsync();
            StreamReader reader = new StreamReader(stream);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(reader.ReadToEnd()));
        }

        public async void save_file(string data, string fileName)
        {
            string targetFile = System.IO.Path.Combine(FileSystem.Current.AppDataDirectory, fileName);

            using FileStream outputStream = File.OpenWrite(targetFile);
            using StreamWriter streamWriter = new(outputStream);

            await streamWriter.WriteAsync(data);
        }
    }
    ...
    public MainPage()
    {
        ...
        api.AddTarget("dialogs", new NativeApi());
    }
    ...

Use added object in JS environment:

    ...
    function openDialog() {
        var promise = window.dialogs.open_file_dialog();
        promise.then((fileData) => {
            let text = atob(fileData);
            console.log(text);
        });
    }

    function saveFile() {
        window.dialogs.save_file("test file", "test.txt");
    }
    ...

## Opportunities, peculiarities and limitations of using

Opportunities:

 - Objects add to JS as Proxy and may be extended
 - C# objects may contains properties and methods
 - Properties can be getted and setted from JS
 - Methods when called from JS can accept arguments and return values
 - Support C# async methods
 - The same object can be added at different addresses and use different internal address schemes (default is "native://") for communication:
 
       ...
       var api = new NativeApi();
       api.AddTarget("api1", api);
       api.AddTarget("api2", api, "myscheme://");
       ...

Peculiarities:

 - Since all calls happen asynchronously, all results return a Promise and return value is passed in to success-function.
 - Some C# errors may passed in JS console.error, but not all.
 
Limitations:

 - For ID of method calling used UUIDv4 - may be conflicts if there are a lot of concurrent calls.
 - Due to the peculiarities of the interaction mechanism, the names of methods and properties can only be used in lowercase.
 - Returned objects must implement JSON serialization interface.
 - Accordingly, nested objects can only be data classes.
 - Due to the fact that WebView::EvaluateJavaScriptAsync can only accept one JS line, if the method returns an object that contains a newline chars after serialization or a string containing a newline chars, then this will cause an error. In this case, recommended that the return value be either cleared of newlines or encoded, for example, using base64 algorithm.
 - Mismatch of types and number of arguments in the call will result in an error.
 
## Licensing

It is MIT license
