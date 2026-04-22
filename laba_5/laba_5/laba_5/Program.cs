using System.Text.Json;

namespace laba_5
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            string storageRoot = Path.GetFullPath("D:\\bsuir\\2_sem\\KSiS\\laba_5\\StorageRoot");
            Directory.CreateDirectory(storageRoot);

            string MapPath(string urlPath)
            {
                urlPath = urlPath.Trim('/');
                return Path.Combine(storageRoot, urlPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            }
            app.MapPut("/{**path}", async (HttpContext ctx, string path) =>
            {
                try
                {
                    string destPath = MapPath(path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                    if (ctx.Request.Headers.TryGetValue("X-Copy-From", out var copyFrom))
                    {
                        string srcPath = MapPath(copyFrom!);

                        if (!File.Exists(srcPath))
                        {
                            ctx.Response.StatusCode = 404;
                            await ctx.Response.WriteAsync("Source file not found");
                            return;
                        }

                        using var src = new FileStream(srcPath, FileMode.Open, FileAccess.Read);
                        using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write);
                        await src.CopyToAsync(dst);

                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("Copied");
                        return;
                    }
                    using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                    {
                        await ctx.Request.Body.CopyToAsync(fs);
                    }

                    ctx.Response.StatusCode = 200;
                    await ctx.Response.WriteAsync($"File '{path}' uploaded successfully.");
                }
                catch (Exception ex)
                {
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.WriteAsync("Error: " + ex.Message);
                }
            });
            app.MapGet("/{**path}", async (HttpContext ctx, string? path) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path))
                        path = "";

                    string fullPath = MapPath(path);

                    if (File.Exists(fullPath))
                    {
                        var fileInfo = new FileInfo(fullPath);
                        ctx.Response.ContentType = "application/octet-stream";
                        ctx.Response.Headers.ContentLength = fileInfo.Length;

                        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                        await fs.CopyToAsync(ctx.Response.Body);
                        return;
                    }
                    if (Directory.Exists(fullPath))
                    {
                        var entries = Directory.GetFileSystemEntries(fullPath);

                        if (entries.Length == 0)
                        {
                            ctx.Response.StatusCode = 200;
                            await ctx.Response.WriteAsync("Empty directory");
                            return;
                        }

                        var files = entries.Select(f => new
                        {
                            name = Path.GetFileName(f),
                            type = Directory.Exists(f) ? "directory" : "file"
                        });

                        ctx.Response.ContentType = "application/json";
                        await ctx.Response.WriteAsync(JsonSerializer.Serialize(files));
                        return;
                    }
                    ctx.Response.StatusCode = 404;
                    await ctx.Response.WriteAsync("File or directory not found");
                }
                catch (Exception ex)
                {
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.WriteAsync("Error: " + ex.Message);
                }
            });

            app.MapMethods("/{**path}", new[] { "HEAD" }, async (HttpContext ctx, string path) =>
            {
                try
                {
                    string fullPath = MapPath(path);

                    if (!File.Exists(fullPath))
                    {
                        ctx.Response.StatusCode = 404;
                        await ctx.Response.WriteAsync("File not found!!");
                        return;
                    }

                    var info = new FileInfo(fullPath);

                    ctx.Response.Headers.ContentLength = info.Length;
                    ctx.Response.Headers.LastModified = info.LastWriteTimeUtc.ToString("R");
                    ctx.Response.ContentType = "application/octet-stream";
                    ctx.Response.StatusCode = 200;
                }
                catch (Exception ex)
                {
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.WriteAsync("Error: " + ex.Message);
                }
            });
            app.MapDelete("/{**path}", async (HttpContext ctx, string path) =>
            {
                try
                {
                    string fullPath = MapPath(path);

                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("File deleted");
                        return;
                    }

                    if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, true);
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("Directory deleted");
                        return;
                    }

                    ctx.Response.StatusCode = 404;
                    await ctx.Response.WriteAsync("Not found");
                }
                catch (Exception ex)
                {
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.WriteAsync("Error: " + ex.Message);
                }
            });

            app.Run("http://127.0.0.2:8542");
        }
    }
}
