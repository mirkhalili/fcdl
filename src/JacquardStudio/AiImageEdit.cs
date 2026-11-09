using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Explicit paid operation; graph execution itself never makes network calls.</summary>
public static class AiImageEdit
{
    private static readonly Uri Endpoint=new("https://api.openai.com/v1/images/edits");
    public static string PromptHash(string prompt) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));
    public static async Task<string> GenerateAsync(BitmapSource input,string prompt,string apiKey,HttpClient? client=null,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(prompt) || prompt.Length>4000) throw new ArgumentException("پرامپت باید ۱ تا ۴۰۰۰ نویسه باشد.");
        if(string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("متغیر محیطی OPENAI_API_KEY تنظیم نشده است.");
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(input)); using var encoded=new MemoryStream(); encoder.Save(encoded);
        if(encoded.Length>25*1024*1024) throw new ArgumentException("تصویر ورودی برای ارسال بیش از ۲۵ مگابایت است.");
        bool owned=client is null; client ??=new HttpClient { Timeout=TimeSpan.FromMinutes(3) };
        try
        {
            using var multipart=new MultipartFormDataContent(); multipart.Add(new StringContent("gpt-image-1"),"model"); multipart.Add(new StringContent(prompt,Encoding.UTF8),"prompt");
            var picture=new ByteArrayContent(encoded.ToArray()); picture.Headers.ContentType=new MediaTypeHeaderValue("image/png"); multipart.Add(picture,"image","design.png");
            using var request=new HttpRequestMessage(HttpMethod.Post,Endpoint) { Content=multipart }; request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",apiKey);
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"پاسخ سرویس هوش مصنوعی ناموفق بود (HTTP {(int)response.StatusCode}). تنظیمات دسترسی و سهمیه را بررسی کنید.");
            if(response.Content.Headers.ContentLength is long n && n>32*1024*1024) throw new InvalidDataException("AI response is too large.");
            using var stream=await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false); using var buffer=new MemoryStream(); var chunk=new byte[8192]; int read;
            while((read=await stream.ReadAsync(chunk,cancellationToken).ConfigureAwait(false))>0) { if(buffer.Length+read>32*1024*1024) throw new InvalidDataException("AI response is too large."); buffer.Write(chunk,0,read); }
            using var json=JsonDocument.Parse(buffer.ToArray());
            if(!json.RootElement.TryGetProperty("data",out var data) || data.ValueKind!=JsonValueKind.Array || data.GetArrayLength()==0 || !data[0].TryGetProperty("b64_json",out var item) || item.ValueKind!=JsonValueKind.String) throw new InvalidDataException("Missing AI output.");
            string encodedImage=item.GetString() ?? throw new InvalidDataException("Missing AI output.");
            byte[] bytes=Convert.FromBase64String(encodedImage); using var imageStream=new MemoryStream(bytes); var decoded=BitmapDecoder.Create(imageStream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
            if((long)decoded.PixelWidth*decoded.PixelHeight>DesignProcessor.MaxPixels) throw new InvalidDataException("AI image exceeds the 4 million pixel limit."); decoded.Freeze(); return ModuleExecutor.EmbedImage(decoded);
        }
        finally { if(owned)client.Dispose(); }
    }
}
