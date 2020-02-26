using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GameOnWeb.Controllers
{
   [ApiController]
   [Route("[controller]")]
   public class GameController : ControllerBase
   {
      private readonly ILogger<GameController> _logger;

      public GameController(ILogger<GameController> logger)
      {
         _logger = logger;
      }

      // This is wrong. Needs to be read from / written to a permanent file.
      private static int UserIdSerialNumber = 0;

      [HttpGet("newuser")]
      public ContentResult GetNewUser()
      {
         // The client sends this when there is no user ID cookie for the server. In that case, we a) create a new cookie from our serial number and b) create the first new game for the user. This makes it easy for people to try out the game. They don't have to register or anything. They just start playing.
         var userId = (++UserIdSerialNumber).ToString();
         ControllerContext.HttpContext.Response.Cookies.Append("userid", userId);
         return base.Content("<div>You made a new <i>user</i> number " + userId + "<div>", "text/html");
      }

      [HttpGet("changeuserid")]
      public ContentResult GetChangeUserId(
         string fromUserId,
         string toUserId)
      {
         // The user ID created by GetNewUser is stored in a cookie on the device they were using at the time. The player can create their own unique user ID string to replace that. That lets them play their games on any device.
         return base.Content("<div>Changed User ID<div>", "text/html");
      }

      [HttpGet("gamelist")]
      public ContentResult GetGameList(
         string userId)
      {
         // This returns the list of all games the user has going. When they pick a game, the client uses GetExistingGame to start playing it.
         return base.Content("<div>Game list<div>", "text/html");
      }

      [HttpGet("newgame")]
      public ContentResult GetNewGame(
         string userId)
      {
         // This creates another game for the existing user.
         return base.Content("<div>New game<div>", "text/html");
      }

      [HttpGet("existinggame")]
      public ContentResult GetExistingGame(
         string userId,
         string gameId)
      {
         // This starts continuing to play a game the user already has, as selected from GetGameList earlier.
         return base.Content("<div>Existing game<div>", "text/html");
      }

      [HttpGet("reaction")]
      public ContentResult GetReaction(
         string userId,
         string gameId,
         string reactionText)
      {
         // After the user sees a game page, they can pick on of the reactions. In response, the server sends a new game page that shows what happened and includes more reaction options.
         return base.Content("<div>Reaction<div>", "text/html");
      }

      [HttpGet("undo")]
      public ContentResult GetUndo(
         string userId,
         string gameId)
      {
         // The lets the user can go back to the previous game page.
         return base.Content("<div>Undo<div>", "text/html");
      }
   }
}
