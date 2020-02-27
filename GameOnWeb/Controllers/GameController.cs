using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Gamebook;

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

      // TODO: This needs to be stored in a permanent database or file.
      private static int UserIdSerialNumber = 0;

      // This lets the HTTP APIs look up the user's game.
      // TODO: There's only one game per user for now.
      // TODO: This needs to go into a permanent database or file.
      private static Dictionary<string, Game> UserContexts = new Dictionary<string, Game>();

      static GameController()
      {
         // Load the static game story. The HTTP APIs use this to generate games for users on clients.
         (FirstUnit, UnitsByUniqueId, ReactionArrowsByUniqueId) = Unit.Load("books\\fo4-2.gb");
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

      private string BuildHtmlContent(
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


      // HTTP API

      private Game GetGameForUser()
      {
         if (!ControllerContext.HttpContext.Request.Cookies.TryGetValue("userid", out var userId))
            // They've never played before.
            return null;

         // If they have a cookie on the client, they've played before. Therefore continue with their previous game.
         return UserContexts[userId];
      }

      [HttpGet("start")]
      public ContentResult GetStart()
      {
         var game = GetGameForUser();

         if (game == null)
         {
            // If there is no user ID cookie set up on the client, we a) create a new cookie from our serial number and b) create the first new game for the user. This makes it easy for people to try out the game. They don't have to register or anything. They just start playing. They can register later if they want.

            // Create a new user ID.
            // TODO: Persist to file.
            var userId = (++UserIdSerialNumber).ToString();

            // Send the user ID to the client as a cookie. All of the HTTP APIs use this user ID value to find the user's games.
            ControllerContext.HttpContext.Response.Cookies.Append("userid", userId);

            // Create a new game for the new user.
            game = new Game(FirstUnit);
            UserContexts.Add(userId, game);
         }

         return base.Content(BuildHtmlContent(game), "text/html");
      }

      [HttpGet("reaction")]
      public ContentResult GetReaction(
         string reactionText)
      {
         // After the user sees a game page, they can pick one of the reactions. In response, the server sends a new game page that shows what happened and includes more reaction options.
         var game = GetGameForUser();
         if (game == null)
         {
            // This shouldn't happen.
            return base.Content("Internal error: Couldn't find any user ID cookie", "text/html");
         }

         game.MoveToReaction(reactionText);
         return base.Content(BuildHtmlContent(game), "text/html");
      }

      [HttpGet("undo")]
      public ContentResult GetUndo(
         string userId,
         string gameId)
      {
         // The lets the user can go back to the previous game page.
         var game = GetGameForUser();
         if (game == null)
         {
            // This shouldn't happen.
            return base.Content("Internal error: Couldn't find any user ID cookie", "text/html");
         }

         game.Undo();
         return base.Content(BuildHtmlContent(game), "text/html");
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
