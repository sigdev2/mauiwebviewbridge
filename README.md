# MAUI WebView bridge from C# to JS

One .cs file for add access to CSharp native objects in MAUI WebView JS scripts for create hybrid single-page applications (SPA).

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

## Opportunities and limitations of using
