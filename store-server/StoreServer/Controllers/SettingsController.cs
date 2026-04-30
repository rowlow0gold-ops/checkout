using Microsoft.AspNetCore.Mvc;
using StoreServer.Models;

namespace StoreServer.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController(StoreDbContext db) : ControllerBase
{
    /// <summary>Returns the current store-wide staff PIN.</summary>
    [HttpGet("staff-pin")]
    public IActionResult GetStaffPin()
    {
        var setting = db.StoreSettings.FirstOrDefault(s => s.Key == "StaffPin");
        return Ok(new { pin = setting?.Value ?? "4312" });
    }

    /// <summary>Updates the store-wide staff PIN. Must be exactly 4 digits.</summary>
    [HttpPut("staff-pin")]
    public IActionResult SetStaffPin([FromBody] SetPinRequest req)
    {
        if (string.IsNullOrEmpty(req.Pin) || req.Pin.Length != 4 || !req.Pin.All(char.IsDigit))
            return BadRequest(new { error = "PIN must be exactly 4 digits." });

        var setting = db.StoreSettings.FirstOrDefault(s => s.Key == "StaffPin");
        if (setting is null)
        {
            db.StoreSettings.Add(new StoreSetting { Key = "StaffPin", Value = req.Pin });
        }
        else
        {
            setting.Value = req.Pin;
        }
        db.SaveChanges();
        return Ok(new { pin = req.Pin });
    }
}

public record SetPinRequest(string Pin);
