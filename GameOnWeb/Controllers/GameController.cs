using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Gamebook;
using System;

namespace GameOnWeb.Controllers
{
   [ApiController]
   [Route("[controller]")]
   public class GameController : ControllerBase
   {
      // VARIABLES AND INITIALIZATION

      // These are set in the static constructor and are used to create new games as requested by clients.
      private static Unit FirstUnit;
      private static Dictionary<string, Unit> UnitsByUniqueId;
      private static Dictionary<string, ReactionArrow> ReactionArrowsByUniqueId;

      // This lets the HTTP APIs look up the user's game.
      // TODO: There's only one game per user for now.
      // TODO: This needs to go into a permanent database or file.
      private static string StartupErrorMessage = "";

      private const string SaveFileExtension = "save.txt";
      private const string GamesInProgressDirectoryName = "games";

      static GameController()
      {
         // Load the static game story. The HTTP APIs use this to generate games for users on clients.
         try
         {
            // TODO: The books folder doesn't get published automatically. Must publish manually. See google for a way to fix this.
            (FirstUnit, UnitsByUniqueId, ReactionArrowsByUniqueId) = Unit.Load("books\\fo4-2.gb");

            // Make sure the directory that contains all the games-in-progress exists.
            Directory.CreateDirectory(GamesInProgressDirectoryName);
         }
         catch (Exception exception)
         {
            StartupErrorMessage = exception.Message;
         }
      }

      // GAME TEXT CREATION

      private string FixQuotes(
        string text)
      {
         // ex. change "hello" to “hello”.
         var result = "";
         // We might want to check the letter before the first letter to see if it is a space.
         var testText = " " + text;
         for (int i = 1; i < testText.Length; ++i)
         {
            switch (testText[i])
            {
               case '"':
                  if (testText[i - 1] == ' ')
                     result += '“';
                  else
                     result += '”';
                  break;
               case var letter:
                  result += letter;
                  break;
            }
         }
         return result;
      }

      private string BuildParagraph(
         string text)
      {
         // We need to add inlines to both Paragraphs and TextBoxes. They have Inlines properties, but you can't pass properties to functions as ref parameters, so we make a list of inlines and add them outside this function.
         text = FixQuotes(text);
         // Em dashes
         text = text.Replace("--", "—");
         // Add a termination marker on the end.
         text += '\0';
         // A pointer into the text.
         int index = 0;
         // An accumulator for the final HTML result string.
         var htmlAccumulator = "";
         // Search for the marker we stuck on the end. It's recursive.
         BuildParagraphTo('\0');
         return htmlAccumulator;

         void BuildParagraphTo(
            char terminator)
         {
            while (true)
            {
               switch (text[index++])
               {
                  case '{':
                     // Reaction link.
                     var terminatorPosition = text.IndexOfAny(new char[] { '}', '\0' }, index);
                     var reactionText = text.Substring(index, terminatorPosition - index);
                     htmlAccumulator += "<a href='ignore' onclick='return onReactionClick(\"" + reactionText + "\");'>";
                     BuildParagraphTo('}');
                     htmlAccumulator += "</a>";
                     break;
                  case '<':
                     // Italic.
                     htmlAccumulator += "<i>";
                     BuildParagraphTo('>');
                     htmlAccumulator += "</i>";
                     break;
                  case Game.PositiveDebugTextStart:
                  case Game.NegativeDebugTextStart:
                     // Debug text.
                     // TODO: This doesn't handle the positive/negative distinction.
                     htmlAccumulator += "<b>";
                     BuildParagraphTo(Game.DebugTextStop);
                     htmlAccumulator += "</b>";
                     break;
                  case '\0':
                     // Could be that the terminator we're looking for is missing, so keep an eye out for the final terminator (which is always there) and stop on that always. Don't increment the index past this. It must stop all missing terminator searches.
                     --index;
                     return;
                  case var letter:
                     if (letter == terminator)
                        return;
                     // Just add all other characters to the end of the HTML.
                     htmlAccumulator += letter;
                     break;
               }
            }
         }
      }

      private string BuildHtml(
         Game game)
      {
         // The Game contains a text version of the game content. This code converts that into HTML for display.
         var result = "";

         var first = true;
         foreach (var paragraphText in game.GetActionText().Split('@'))
         {
            if (first && paragraphText.Length < 1)
               continue;
            first = false;
            result += "<p>" + BuildParagraph(paragraphText) + "</p>";
         }

         result += "<ul>";
         foreach (var reactionText in game.GetReactionTextsByScore())
            result += "<li>" + BuildParagraph("{" + reactionText + "}") + "</li>";
         result += "</ul>";

         if (game.CanUndo())
         {
            result += "<a href='ignore' onclick='return onUndo();'>Go back</a>";
         }

         return result;
      }


      // HTTP API SUPPORT ROUTINES

      private void AddToMiniLog(
         ref string miniLog,
         string message)
      {
         miniLog += String.Format("<p>{0}</p>", message);
      }

      private void SaveGameToFile(
         string gameFilePath,
         Game game,
         ref string miniLog)
      {
         using var writer = new StreamWriter(gameFilePath, false);
         game.Save(writer);
         AddToMiniLog(ref miniLog, "Saved game in " + gameFilePath);
      }

      private ContentResult BuildContent(
         string contentString,
         string miniLog)
      {
         return base.Content(miniLog + contentString, "text/html");
      }

      private (string, Game) GetGameForUserCookie(
         ref string miniLog)
      {
         // First make sure they have a user ID cookie.
         if (!ControllerContext.HttpContext.Request.Cookies.TryGetValue("userid", out var userId))
         {
            // They have no userid cookie. Generate a user ID and send it to the client as a cookie. All of the HTTP APIs use this user ID value to find the user's games.
            userId = Guid.NewGuid().ToString();
            ControllerContext.HttpContext.Response.Cookies.Append("userid", userId);
            AddToMiniLog(ref miniLog, "No userid cookie. Generated " + userId);
         }
         else
            AddToMiniLog(ref miniLog, "Using existing userid cookie " + userId);

         // Next, make sure there's a game for the user ID.
         Game game;
         var gameFilePath = Path.Combine(GamesInProgressDirectoryName, userId + "." + SaveFileExtension);
         AddToMiniLog(ref miniLog, "check game file path exists: " + gameFilePath);
         if (System.IO.File.Exists(gameFilePath))
         {
            // Load the existing game.
            using var reader = new StreamReader(gameFilePath);
            game = new Game(reader, UnitsByUniqueId, ReactionArrowsByUniqueId);
            AddToMiniLog(ref miniLog, "Loaded existing game.");
         }
         else
         {
            // Create a new game for the new user and save it to disk.
            game = new Game(FirstUnit);
            AddToMiniLog(ref miniLog, "Created new game.");
            SaveGameToFile(gameFilePath, game, ref miniLog);
         }

         // There is now a loaded game in memory and a game on disk.
         return (gameFilePath, game);
      }


      // HTTP API

      [HttpGet("start")]
      public ContentResult GetStart()
      {
         // Start a new game or continue an existing game.
         var miniLog = "";
         AddToMiniLog(ref miniLog, "In start.");

         if (StartupErrorMessage != "")
            return BuildContent("Error on startup: " + StartupErrorMessage, miniLog);

         (var gameFilePath, var game) = GetGameForUserCookie(ref miniLog);
         return BuildContent(BuildHtml(game), miniLog);
      }

      [HttpGet("reaction")]
      public ContentResult GetReaction(
         string reactionText)
      {
         // After the user sees a game page, they can pick one of the reactions. In response, the server sends a new game page that shows what happened and includes more reaction options.
         var miniLog = "";
         AddToMiniLog(ref miniLog, "In reaction.");

         if (StartupErrorMessage != "")
            return BuildContent("Error on startup: " + StartupErrorMessage, miniLog);

         (var gameFilePath, var game) = GetGameForUserCookie(ref miniLog);
         game.MoveToReaction(reactionText);

         // Save game state to disk.
         SaveGameToFile(gameFilePath, game, ref miniLog);

         return BuildContent(BuildHtml(game), miniLog);
      }

      [HttpGet("undo")]
      public ContentResult GetUndo(
         string userId,
         string gameId)
      {
         // The lets the user can go back to the previous game page.
         var miniLog = "";
         AddToMiniLog(ref miniLog, "In undo.");

         // After the user sees a game page, they can pick one of the reactions. In response, the server sends a new game page that shows what happened and includes more reaction options.
         if (StartupErrorMessage != "")
            return BuildContent("Error on startup: " + StartupErrorMessage, miniLog);

         (var gameFilePath, var game) = GetGameForUserCookie(ref miniLog);
         game.Undo();

         // Save game state to disk.
         SaveGameToFile(gameFilePath, game, ref miniLog);

         return BuildContent(BuildHtml(game), miniLog);
      }

      [HttpGet("clean")]
      public ContentResult GetClean()
      {
         // This is for testing only. It clears the user ID cookie so you can test a clean startup.
         var miniLog = "";
         AddToMiniLog(ref miniLog, "In clean.");

         ControllerContext.HttpContext.Response.Cookies.Delete("userid");

         return BuildContent("", miniLog);
      }

      [HttpGet("changeuserid")]
      public ContentResult GetChangeUserId(
         string fromUserId,
         string toUserId)
      {
         // The user ID created by GetNewUser is stored in a cookie on the device they were using at the time. The player can create their own unique user ID string to replace that. That lets them play their games on any device.
         return base.Content("<div>Change User ID TBD<div>", "text/html");
      }

      [HttpGet("gamelist")]
      public ContentResult GetGameList(
         string userId)
      {
         // This returns the list of all games the user has going. When they pick a game, the client uses GetExistingGame to start playing it.
         return base.Content("<div>Game list TBD<div>", "text/html");
      }

      [HttpGet("existinggame")]
      public ContentResult GetExistingGame(
         string userId,
         string gameId)
      {
         // This starts continuing to play a game the user already has, as selected from GetGameList earlier.
         return base.Content("<div>Existing game<div>", "text/html");
      }


      // OTHER STUFF

      // Hmm. I wonder what this logging stuff does?
      private readonly ILogger<GameController> _logger;

      public GameController(ILogger<GameController> logger)
      {
         _logger = logger;
      }
   }
}
