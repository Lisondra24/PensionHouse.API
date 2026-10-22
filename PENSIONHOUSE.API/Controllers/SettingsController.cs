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
    private readonly string _connStr;

    public SettingsController(IConfiguration config)
    {
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
            businessContact = V("BusinessContact")
        });
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