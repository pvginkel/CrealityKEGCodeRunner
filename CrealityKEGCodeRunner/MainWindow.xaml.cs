using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrealityKEGCodeRunner;

internal partial class MainWindow
{
    private const string ScriptFileName = "Ad-Hoc Script.gcode";

    private CrealityClient? _client;

    public MainWindow()
    {
        InitializeComponent();

        UpdateEnabled();

        using var key = App.CreateBaseKey();

        _url.Text = key.GetValue("URL") as string ?? "";
        _gcode.Text = key.GetValue("GCode") as string ?? "";

        if (key.GetValue("Bounds") is string bounds)
        {
            var parts = bounds.Split(',');
            if (parts is [var maximized, var left, var top, var width, var height])
            {
                Left = double.Parse(left, CultureInfo.InvariantCulture);
                Top = double.Parse(top, CultureInfo.InvariantCulture);
                Width = double.Parse(width, CultureInfo.InvariantCulture);
                Height = double.Parse(height, CultureInfo.InvariantCulture);

                if (int.Parse(maximized) != 0)
                    WindowState = WindowState.Maximized;
            }
        }
    }

    private void UpdateEnabled()
    {
        _send.IsEnabled =
            _url.Text.Length > 0 && Uri.TryCreate(_url.Text, UriKind.Absolute, out var _);
    }

    private void _url_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateEnabled();
    }

    private async void _send_Click(object sender, RoutedEventArgs e)
    {
        using (var key = App.CreateBaseKey())
        {
            key.SetValue("URL", _url.Text);
            key.SetValue("GCode", _gcode.Text);
        }

        _status.Content = "Uploading...";

        var gcode = _gcode.SelectionLength > 0 ? _gcode.SelectedText : _gcode.Text;

        using (var content = new MultipartFormDataContent($"BOUNDARY----{Guid.NewGuid()}"))
        {
            content.Add(new StringContent(gcode), "file", ScriptFileName);

            using (
                var message = await App.HttpClient.PostAsync(
                    GetUrl($"upload/{Uri.EscapeDataString(ScriptFileName)}"),
                    content
                )
            )
            {
                message.EnsureSuccessStatusCode();
            }
        }

        _status.Content = "Running...";

        _client?.Dispose();
        _client = new CrealityClient(_url.Text);

        _client.DataReceived += _client_DataReceived;

        await _client.Connect();

        await _client.SendAsync(
            new JObject
            {
                ["method"] = "set",
                ["params"] = new JObject
                {
                    ["opGcodeFile"] = $"printprt:/usr/data/printer_data/gcodes/{ScriptFileName}"
                }
            }.ToString(Formatting.None)
        );
    }

    private async void _client_DataReceived(object? sender, CrealityDataReceivedEventArgs e)
    {
        if (!e.Data.StartsWith("{"))
            return;

        var obj = JObject.Parse(e.Data);

        if (obj.TryGetValue("printProgress", out var value))
            Dispatcher.BeginInvoke(() => _status.Content = $"Progress: {value}%");

        if (obj.TryGetValue("err", out var err))
        {
            var errorObj = (JObject)err;
            var errorCode = (int?)errorObj["errcode"];
            var key = (int?)errorObj["key"];

            if (errorCode.GetValueOrDefault() != 0 || key.GetValueOrDefault() != 0)
                Dispatcher.BeginInvoke(() => _status.Content = $"E{errorCode} key: {key}");

            var client = _client;

            if (client != null)
            {
                await client.SendAsync(
                    new JObject
                    {
                        ["method"] = "set",
                        ["params"] = new JObject { ["cleanErr"] = 1 }
                    }.ToString(Formatting.None)
                );
            }
        }
    }

    private string? GetUrl(string url)
    {
        var uri = new Uri(_url.Text);

        return $"http://{uri.Host}/{url.TrimStart('/')}";
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        using var key = App.CreateBaseKey();

        if (WindowState == WindowState.Maximized)
        {
            key.SetValue(
                "Bounds",
                $"1,{RestoreBounds.Left.ToString(CultureInfo.InvariantCulture)},{RestoreBounds.Top.ToString(CultureInfo.InvariantCulture)},{RestoreBounds.Width.ToString(CultureInfo.InvariantCulture)},{RestoreBounds.Height.ToString(CultureInfo.InvariantCulture)}"
            );
        }
        else
        {
            key.SetValue(
                "Bounds",
                $"0,{Left.ToString(CultureInfo.InvariantCulture)},{Top.ToString(CultureInfo.InvariantCulture)},{Width.ToString(CultureInfo.InvariantCulture)},{Height.ToString(CultureInfo.InvariantCulture)}"
            );
        }
    }
}
