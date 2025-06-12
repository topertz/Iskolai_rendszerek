using Microsoft.AspNetCore.Mvc;
using SchoolAPI.Models.Lunch;
using SchoolAPI.Models;
using System.Collections.Generic;
using System.Data.SQLite;
using SchoolAPI.Controllers;

namespace SchoolAPI.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class LunchController : Controller
    {
        [HttpGet]
        public IActionResult GetMenu()
        {
            var menu = new List<object>();
            var sessionId = Request.Cookies["id"];

            if (string.IsNullOrEmpty(sessionId))
            return Unauthorized("Nincs bejelentkezve.");
             var userId = SessionManager.GetUserID(sessionId);

            if (userId == -1)
                return Unauthorized("Nincs bejelentkezve vagy lejárt a munkamenet.");

            string sql = @"
            SELECT 
                Lunch.LunchID,
                Lunch.Day,
                Soup.Name AS Soup,
                MainDish.Name AS MainDish,
                Dessert.Name AS Dessert,
                CASE 
                    WHEN EXISTS (
                        SELECT 1 FROM LunchSignup 
                        WHERE LunchSignup.UserID = @userId AND LunchSignup.LunchID = Lunch.LunchID
                    ) THEN 1
                    ELSE 0
                END AS IsSignedUp
            FROM Lunch
            JOIN Soup ON Lunch.SoupID = Soup.SoupID
            JOIN MainDish ON Lunch.MainDishID = MainDish.MainDishID
            JOIN Dessert ON Lunch.DessertID = Dessert.DessertID
            ORDER BY CASE 
                WHEN Lunch.Day = 'Hétfő' THEN 1
                WHEN Lunch.Day = 'Kedd' THEN 2
                WHEN Lunch.Day = 'Szerda' THEN 3
                WHEN Lunch.Day = 'Csütörtök' THEN 4
                WHEN Lunch.Day = 'Péntek' THEN 5
                ELSE 6 END";

            using (var connection = DatabaseConnector.CreateNewConnection())
            using (var cmd = new SQLiteCommand(sql, connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId);
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    menu.Add(new
                    {
                        LunchID = reader.GetInt32(0),
                        Day = reader.GetString(1),
                        Soup = reader.GetString(2),
                        MainDish = reader.GetString(3),
                        Dessert = reader.GetString(4),
                        IsSignedUp = reader.GetInt32(5) == 1
                    });
                }
            }

            return Json(menu);
        }

        [HttpPost]
        public IActionResult Regenerate()
        {
            using var conn = DatabaseConnector.CreateNewConnection();
            LunchGenerator.GenerateWeeklyLunch(conn);
            return Ok("Lunch menu regenerated");
        }

        [HttpPost]
        public IActionResult UpdateDayMenu([FromBody] DayMenuUpdateDto update)
        {
            using var conn = DatabaseConnector.CreateNewConnection();

            int? soupId = GetIdByName(conn, "Soup", update.Soup);
            int? mainDishId = GetIdByName(conn, "MainDish", update.MainDish);
            int? dessertId = GetIdByName(conn, "Dessert", update.Dessert);

            if (soupId == null || mainDishId == null || dessertId == null)
            {
                return BadRequest("Étel nem található az adatbázisban.");
            }

            var cmd = new SQLiteCommand(@"
            UPDATE Lunch 
            SET SoupID = @soupId, MainDishID = @mainId, DessertID = @dessertId 
            WHERE Day = @day", conn);

            cmd.Parameters.AddWithValue("@soupId", soupId);
            cmd.Parameters.AddWithValue("@mainId", mainDishId);
            cmd.Parameters.AddWithValue("@dessertId", dessertId);
            cmd.Parameters.AddWithValue("@day", update.Day);

            int rowsAffected = cmd.ExecuteNonQuery();

            if (rowsAffected == 0)
            {
                return NotFound("A megadott nap nem található.");
            }

            return Ok("Mentés sikeres.");
        }

        private int? GetIdByName(SQLiteConnection conn, string table, string? name)
        {
            var cmd = new SQLiteCommand($"SELECT {table}ID FROM {table} WHERE Name = @name", conn);
            cmd.Parameters.AddWithValue("@name", name);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : (int?)null;
        }
        [HttpPost]
        public IActionResult SignUp([FromBody] SignUpDto data)
        {
            var sessionId = Request.Cookies["id"];
            if (string.IsNullOrEmpty(sessionId))
                return Unauthorized("Nincs bejelentkezve.");

            var userId = SessionManager.GetUserID(sessionId);
            if (userId == -1)
                return Unauthorized("Nincs bejelentkezve vagy lejárt a munkamenet.");

            using var conn = DatabaseConnector.CreateNewConnection();

            var cmdGetLunchId = new SQLiteCommand("SELECT LunchID FROM Lunch WHERE Day = @day", conn);
            cmdGetLunchId.Parameters.AddWithValue("@day", data.Day);

            var lunchIdObj = cmdGetLunchId.ExecuteScalar();
            if (lunchIdObj == null)
                return NotFound("Nem található ebéd a megadott napra.");

            int lunchId = Convert.ToInt32(lunchIdObj);

            var cmd = new SQLiteCommand("INSERT INTO LunchSignup (UserID, LunchID, Day) VALUES (@userId, @lunchId, @day)", conn);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@lunchId", lunchId);
            cmd.Parameters.AddWithValue("@day", data.Day);

            cmd.ExecuteNonQuery();

            return Ok("Feljelentkezés sikeres.");
        }
        [HttpPost]
        public IActionResult SignOut([FromBody] SignUpDto data)
        {
            var sessionId = Request.Cookies["id"];
            if (string.IsNullOrEmpty(sessionId))
                return Unauthorized("Nincs bejelentkezve.");

            var userId = SessionManager.GetUserID(sessionId);
            if (userId == -1)
                return Unauthorized("Nincs bejelentkezve vagy lejárt a munkamenet.");

            using var conn = DatabaseConnector.CreateNewConnection();

            var cmdGetLunchId = new SQLiteCommand("SELECT LunchID FROM Lunch WHERE Day = @day", conn);
            cmdGetLunchId.Parameters.AddWithValue("@day", data.Day);

            var lunchIdObj = cmdGetLunchId.ExecuteScalar();
            if (lunchIdObj == null)
                return NotFound("Nem található ebéd a megadott napra.");

            int lunchId = Convert.ToInt32(lunchIdObj);

            var cmd = new SQLiteCommand("DELETE FROM LunchSignup WHERE UserID = @userId AND LunchID = @lunchId", conn);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@lunchId", lunchId);

            int affected = cmd.ExecuteNonQuery();

            return affected > 0 ? Ok("Lejelentkezés sikeres.") : NotFound("Nem volt ilyen jelentkezés.");
        }
    }
    public class DayMenuUpdateDto
    {
        public string? Day { get; set; }
        public string? Soup { get; set; }
        public string? MainDish { get; set; }
        public string? Dessert { get; set; }
    }
    public class SignUpDto
    {
        public string? Day { get; set; }
    }
}