using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

public record SaveSettingsRequest(
    string? GcashNumber, string? GcashName,
    string? BusinessName, string? BusinessAddress, string? BusinessContact);

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private const long MaxQrBytes = 3 * 1024 * 1024;   // 3 MB

    private readonly string _connStr;
    private readonly IWebHostEnvironment _env;

    public SettingsController(IConfiguration config, IWebHostEnvironment env)
    {
        _env = env;
        _connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");
    }

    private SqlConnection Conn() => new SqlConnection(_connStr);

    private class Row { public string SettingKey { get; set; } = ""; public string? SettingValue { get; set; } }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        using var conn = Conn();
        var rows = (await conn.QueryAsync<Row>("SELECT SettingKey, SettingValue FROM AppSettings"))
                   .ToDictionary(r => r.SettingKey, r => r.SettingValue);

        string? V(string k) => rows.TryGetValue(k, out var v) ? v : null;
        return Ok(new
        {
            gcashNumber = V("GcashNumber"),
            gcashName = V("GcashName"),
            businessName = V("BusinessName"),
            businessAddress = V("BusinessAddress"),
            businessContact = V("BusinessContact"),
            // The QR image itself is served by GET api/settings/gcash-qr.
            // gcashQrVersion changes on every upload so the app knows to reload it.
            hasGcashQr = !string.IsNullOrEmpty(V("GcashQrFile")),
            gcashQrVersion = V("GcashQrFile")
        });
    }

    // ------------------------------------------------------------------
    // GCash QR code image (the owner's "Receive Money" QR)
    // ------------------------------------------------------------------

    private string QrDir => Path.Combine(_env.ContentRootPath, "PrivateUploads", "gcash");

    private async Task<string?> GetQrFileNameAsync()
    {
        using var conn = Conn();
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'GcashQrFile'");
    }

    private async Task SetQrFileNameAsync(string? fileName)
    {
        const string merge = @"
            MERGE AppSettings AS t
            USING (SELECT 'GcashQrFile' AS SettingKey, @Value AS SettingValue) AS s
               ON t.SettingKey = s.SettingKey
            WHEN MATCHED THEN UPDATE SET SettingValue = s.SettingValue
            WHEN NOT MATCHED THEN INSERT (SettingKey, SettingValue) VALUES (s.SettingKey, s.SettingValue);";
        using var conn = Conn();
        await conn.ExecuteAsync(merge, new { Value = fileName });
    }

    // Any signed-in user (guests need it at checkout)
    [Authorize]
    [HttpGet("gcash-qr")]
    public async Task<IActionResult> GetGcashQr()
    {
        var name = await GetQrFileNameAsync();
        if (string.IsNullOrEmpty(name)) return NotFound(new { message = "No GCash QR has been uploaded yet." });

        var path = Path.Combine(QrDir, Path.GetFileName(name));   // GetFileName blocks path tricks
        if (!System.IO.File.Exists(path)) return NotFound(new { message = "No GCash QR has been uploaded yet." });

        var contentType = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        Response.Headers["Cache-Control"] = "private, max-age=3600";
        return PhysicalFile(path, contentType);
    }

    // Admin only: upload or replace the QR (multipart/form-data, field name "image")
    [Authorize(Roles = "admin")]
    [HttpPost("gcash-qr")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(4_000_000)]
    public async Task<IActionResult> UploadGcashQr(IFormFile image)
    {
        if (image is null || image.Length == 0 || image.Length > MaxQrBytes)
            return BadRequest(new { message = "The QR image must be between 1 byte and 3 MB." });

        var ext = await DetectImageExtensionAsync(image);
        if (ext is null)
            return BadRequest(new { message = "The QR image must be a JPG or PNG." });

        Directory.CreateDirectory(QrDir);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        await using (var fs = System.IO.File.Create(Path.Combine(QrDir, fileName)))
            await image.CopyToAsync(fs);

        var old = await GetQrFileNameAsync();
        await SetQrFileNameAsync(fileName);

        if (!string.IsNullOrEmpty(old))
        {
            var oldPath = Path.Combine(QrDir, Path.GetFileName(old));
            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        }
        return Ok(new { message = "GCash QR saved.", gcashQrVersion = fileName });
    }

    // Admin only: remove the QR
    [Authorize(Roles = "admin")]
    [HttpDelete("gcash-qr")]
    public async Task<IActionResult> DeleteGcashQr()
    {
        var old = await GetQrFileNameAsync();
        await SetQrFileNameAsync(null);
        if (!string.IsNullOrEmpty(old))
        {
            var oldPath = Path.Combine(QrDir, Path.GetFileName(old));
            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        }
        return Ok(new { message = "GCash QR removed." });
    }

    // Checks the real file header, not just the file name
    private static async Task<string?> DetectImageExtensionAsync(IFormFile file)
    {
        var head = new byte[8];
        await using var s = file.OpenReadStream();
        var read = await s.ReadAsync(head.AsMemory(0, 8));
        if (read < 4) return null;

        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ".jpg";
        if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return ".png";
        return null;
    }

    [Authorize(Roles = "admin")]
    [HttpPut]
    public async Task<IActionResult> Save(SaveSettingsRequest req)
    {
        string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        var gNum = Clean(req.GcashNumber);
        var gName = Clean(req.GcashName);
        var bName = Clean(req.BusinessName);
        var bAddr = Clean(req.BusinessAddress);
        var bCont = Clean(req.BusinessContact);

        var phone = new Regex(@"^[0-9+\-\s]{7,20}$");
        if (gNum is not null && !phone.IsMatch(gNum))
            return BadRequest(new { message = "Enter a valid GCash number." });
        if (bCont is not null && !phone.IsMatch(bCont))
            return BadRequest(new { message = "Enter a valid contact number." });
        if ((gName?.Length ?? 0) > 100 || (bName?.Length ?? 0) > 100 || (bAddr?.Length ?? 0) > 300)
            return BadRequest(new { message = "One of the fields is too long." });

        const string merge = @"
            MERGE AppSettings AS t
            USING (SELECT @Key AS SettingKey, @Value AS SettingValue) AS s
               ON t.SettingKey = s.SettingKey
            WHEN MATCHED THEN UPDATE SET SettingValue = s.SettingValue
            WHEN NOT MATCHED THEN INSERT (SettingKey, SettingValue) VALUES (s.SettingKey, s.SettingValue);";

        using var conn = Conn();
        await conn.ExecuteAsync(merge, new[]
        {
            new { Key = "GcashNumber",     Value = gNum },
            new { Key = "GcashName",       Value = gName },
            new { Key = "BusinessName",    Value = bName },
            new { Key = "BusinessAddress", Value = bAddr },
            new { Key = "BusinessContact", Value = bCont }
        });

        return Ok(new { message = "Settings saved." });
    }
}