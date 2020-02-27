using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Gamebook
{
   // Page is a finished page, ready for display, plus all the information necessary to create the next page.
   public class Page
   {
      public class ScoredReactionArrow
      {
         public double Score;
         public ReactionArrow ReactionArrow;

         public ScoredReactionArrow(
            double score,
            ReactionArrow reactionArrow)
         {
            Score = score;
            ReactionArrow = reactionArrow;
         }
      }

      // The settings contain the state of the game: where you are, what people think of you, etc.
      public Dictionary<string, Setting> Settings = new Dictionary<string, Setting>();

      // Stack of return merge locations.
      public Stack<Unit> NextTargetUnitOnReturn = new Stack<Unit>();

      // This is the body of the text on the screen.
      public string ActionText;

      // The keys are the reaction texts that appear below the action text. The reaction arrow data is used by the game to transition to the next unit.
      public Dictionary<string, ScoredReactionArrow> Reactions;


      public Page() { }

      public Page(
         Page other)
      {
         // Make a copy of the other page which is completely detached and independent. We need this to push copies of pages on the stack.
         Settings = new Dictionary<string, Setting>(other.Settings);

         // C# stack copy flips the stack, so reverse that.
         NextTargetUnitOnReturn = new Stack<Unit>(other.NextTargetUnitOnReturn.Reverse());

         // Immutable so no need to duplicate.
         ActionText = other.ActionText;

         // Don't need to duplicate the reaction arrows; they are immutable.
         Reactions = new Dictionary<string, ScoredReactionArrow>(other.Reactions);

      }

      public const char SaveFileDelimiter = '∙';

      public void Save(
         StreamWriter writer)
      {
         foreach (var setting in Settings)
         {
            writer.Write("s" + SaveFileDelimiter + setting.Key + SaveFileDelimiter);
            switch (setting.Value)
            {
               case StringSetting stringSetting:
                  writer.WriteLine("s" + SaveFileDelimiter + stringSetting.Value);
                  break;
               case ScoreSetting scoreSetting:
                  writer.WriteLine("c" + SaveFileDelimiter + scoreSetting.GetChosenCount().ToString() + SaveFileDelimiter + scoreSetting.GetOpportunityCount());
                  break;
               case BooleanSetting booleanSetting:
                  writer.WriteLine("b" + SaveFileDelimiter + (booleanSetting.Value ? "1" : "0"));
                  break;
            }
         }

         foreach (var unit in NextTargetUnitOnReturn)
            writer.WriteLine("n" + SaveFileDelimiter + unit.UniqueId);

         writer.WriteLine("a" + SaveFileDelimiter + ActionText);

         foreach (var reaction in Reactions)
            writer.WriteLine("r" + SaveFileDelimiter + reaction.Value.ReactionArrow.UniqueId + SaveFileDelimiter + reaction.Value.Score + SaveFileDelimiter + reaction.Key);

         writer.WriteLine("x");
      }

      public static Page TryLoad(
         StreamReader reader,
         Dictionary<string, Unit> unitsByUniqueId,
         Dictionary<string, ReactionArrow> reactionArrowsByUniqueId)
      {
         var result = new Page();
         result.ActionText = null;
         result.Reactions = new Dictionary<string, ScoredReactionArrow>();

         // End of file right at the beginning (maybe after an 'x' operation) indicates a valid end of the file.
         var line = reader.ReadLine();
         if (line == null)
            return null;

         while (true)
         {
            var parts = line.Split(SaveFileDelimiter);
            switch (parts[0])
            {
               case "s":
                  Setting setting;
                  switch (parts[2])
                  {
                     case "s":
                        setting = new StringSetting(parts[3]);
                        break;
                     case "c":
                        if (!int.TryParse(parts[3], out var chosen))
                           throw new InvalidOperationException(string.Format($"Can't parse int chosen '{parts[3]}'."));
                        if (!int.TryParse(parts[4], out var opportunity))
                           throw new InvalidOperationException(string.Format($"Can't parse int opportunity '{parts[4]}'."));
                        setting = new ScoreSetting(chosen, opportunity);
                        break;
                     case "b":
                        switch (parts[3])
                        {
                           case "0":
                              setting = new BooleanSetting(false);
                              break;
                           case "1":
                              setting = new BooleanSetting(true);
                              break;
                           default:
                              throw new InvalidOperationException(string.Format($"Unexpected boolean value '{parts[3]}'."));
                        }
                        break;
                     default:
                        throw new InvalidOperationException(string.Format($"Unexpected setting type '{parts[2]}'."));
                  }
                  result.Settings.Add(parts[1], setting);
                  break;
               case "n":
                  result.NextTargetUnitOnReturn.Push(unitsByUniqueId[parts[1]]);
                  break;
               case "a":
                  result.ActionText = parts[1];
                  break;
               case "r":
                  if (!double.TryParse(parts[2], out var score))
                     throw new InvalidOperationException(string.Format($"Can't parse double score '{parts[2]}'."));
                  result.Reactions.Add(parts[3], new ScoredReactionArrow(score, reactionArrowsByUniqueId[parts[1]]));
                  break;
               case "x":
                  // Flip the stack so it's going the right way. When you copy a stack, it flips it.
                  result.NextTargetUnitOnReturn = new Stack<Unit>(result.NextTargetUnitOnReturn);
                  return result;
               default:
                  throw new InvalidOperationException(string.Format($"Unexpected operation '{parts[0]}'."));
            }
            line = reader.ReadLine();
            if (line == null)
               throw new InvalidOperationException(string.Format($"Unexpected end of save file."));
         }
      }

      public Page BuildNext(
         string reactionText)
      {
         // Start the next page off as a copy of this page.
         Page nextPage = new Page(this);

         // Get the chosen arrow.
         if (!nextPage.Reactions.TryGetValue(reactionText, out var chosen))
            throw new InvalidOperationException(string.Format($"No arrow for reaction '{reactionText}'."));

         var trace = "";

         // Add to the score counts for all the offered arrows.
         foreach (var offered in nextPage.Reactions.Values)
         {
            offered.ReactionArrow.Code.Traverse((code, originalSourceText) =>
            {
               if (!(code is ScoreCode scoreCode) || scoreCode.SortOnly)
                  return true;
               foreach (var id in scoreCode.Ids)
               {
                  var scoreSetting = nextPage.Settings[id] as ScoreSetting;
                  var oldRatio = scoreSetting.RatioString();
                  scoreSetting.RaiseOpportunityCount();
                  bool italics = false;
                  if (offered.ReactionArrow == chosen.ReactionArrow)
                  {
                     italics = true;
                     scoreSetting.RaiseChosenCount();
                  }
                  if (Game.DebugMode)
                     trace += string.Format($"@{Game.PositiveDebugTextStart}{(italics ? "<" : "")}{id} {oldRatio} → {scoreSetting.RatioString()} {scoreSetting.PercentString()} {(scoreSetting.Value ? "true" : "false")}{(italics ? ">" : "")}{Game.DebugTextStop}");

               }
               return true;
            });
         }

         // Make a little report for debugging purposes.
         var sortDictionary = new Dictionary<double, string>();
         double uniquifier = 0.0;
         foreach (var setting in nextPage.Settings)
         {
            // Ex. brave    43% (3/7) 
            if (setting.Value is ScoreSetting scoreSetting)
            {
               string line = String.Format($"{setting.Key,-12} {scoreSetting.PercentString()} ({scoreSetting.RatioString()}) {(scoreSetting.Value ? "true" : "false")}");
               sortDictionary.Add(scoreSetting.ScoreValue + uniquifier, line);
               uniquifier += 0.00001;
            }
         }
         var scoresReportWriter = new StreamWriter("scores.txt", false);
         foreach (var line in sortDictionary.OrderByDescending(pair => pair.Key).Select(pair => pair.Value))
         {
            scoresReportWriter.WriteLine(line);
         }
         scoresReportWriter.Close();

         // Move to the unit the chosen arrow points to.
         nextPage.Build(chosen.ReactionArrow.TargetUnit, trace);
         return nextPage;
      }

      public void Build(
         Unit firstUnit,
         string startingTrace)
      {
         // Starting with the current unit box, a) merge the texts of all units connected below it into one text, and b) collect all the reaction arrows.
         var accumulatedReactions = new Dictionary<string, ScoredReactionArrow>();
         // The action text will contain all the merged action texts.
         var accumulatedActionTexts = startingTrace;
         // If there were no reaction arrows, we've reached an end point and need to return to 
         var gotAReactionArrow = false;
         // Reactions are sorted by score, which is a floating point number. But some reactions may have the same score. So add a small floating-point sequence number to each one, to disambiguate them.
         double reactionScoreDisambiguator = 0;

         // Scores use this to compute whether you are above average in a score. Set it now, before creating the page, so it can be used in conditions evaluated throughout page creation.
         ScoreSetting.Average = Settings.Values.Where(setting => setting is ScoreSetting).Select(setting => (setting as ScoreSetting).ScoreValue).DefaultIfEmpty().Average();
         if (Game.DebugMode)
            accumulatedActionTexts += String.Format($"@{Game.PositiveDebugTextStart}average = {ScoreSetting.Average:0.00}%{Game.DebugTextStop}");

         // This recursive routine will accumulate all the action and reaction text values in the above variables.
         Accumulate(firstUnit);
         while (!gotAReactionArrow)
         {
            // We got to a dead end without finding any reaction options for the player. So pop back to a pushed location and continue merging from there.
            if (!NextTargetUnitOnReturn.Any())
               throw new InvalidOperationException(string.Format($"Got to a dead end with no place to return to."));
            var unit = NextTargetUnitOnReturn.Pop();
            if (Game.DebugMode)
               accumulatedActionTexts += "@" + Game.PositiveDebugTextStart + "pop " + unit.UniqueId + Game.DebugTextStop;
            Accumulate(unit);
         }

         ActionText = FixPlus(accumulatedActionTexts);
         Reactions = accumulatedReactions;

         void Accumulate(
            Unit unit)
         {
            // First append this action box's own text and execute any settings.
            if (accumulatedActionTexts.Length == 0)
               accumulatedActionTexts = EvaluateText(unit.ActionCode);
            else
               accumulatedActionTexts += " " + EvaluateText(unit.ActionCode);

            EvaluateSettingsAndScores(unit.ActionCode, out string trace1);
            accumulatedActionTexts += trace1;

            // Next examine all the arrows for the action.
            var allWhensFailed = true;
            var whenElseArrows = new List<Arrow>();
            var returnArrows = new List<ReturnArrow>();
            foreach (var arrow in unit.GetArrows())
            {
               if (arrow is ReturnArrow returnArrow)
                  // We'll deal with these return arrows at the end of the loop.
                  returnArrows.Add(returnArrow);
               else if (EvaluateWhenElse(arrow.Code))
                  // Save 'when else' arrows for possible later execution.
                  whenElseArrows.Add(arrow);
               else
               {
                  // If conditions in the arrow are false, then just ignore the arrow completely. This includes all types of arrows.
                  (var succeeded, var hadWhen) = EvaluateWhen(arrow.Code, out string trace2);
                  accumulatedActionTexts += trace2;
                  if (!succeeded)
                     continue;
                  if (hadWhen)
                     allWhensFailed = false;
                  AccumulateArrow(arrow);
               }
            }
            if (allWhensFailed)
               // If none of the 'when EXPRESSIONS' arrows succeeded, execute the 'when else' arrows now.
               foreach (var arrow in whenElseArrows)
                  AccumulateArrow(arrow);
            if (returnArrows.Any())
            {
               // Create a unit on the fly and push it on the stack for execution on return. This converts the return arrows to merge arrows.
               var returnUnit = Unit.BuildReturnUnitFor(returnArrows);
               NextTargetUnitOnReturn.Push(returnUnit);
            }
         }

         void AccumulateArrow(
         Arrow arrow)
         {
            switch (arrow)
            {
               case MergeArrow mergeArrow:
                  // There may be 'set' parameters for a referential merge.
                  EvaluateSettingsAndScores(arrow.Code, out string trace);
                  accumulatedActionTexts += trace;

                  if (Game.DebugMode)
                     accumulatedActionTexts += "@" + Game.PositiveDebugTextStart + "merge" + (mergeArrow.DebugSceneId != null ? " " + mergeArrow.DebugSceneId : "") + Game.DebugTextStop;

                  // There are two kinds of merge arrows.
                  Unit targetUnit;
                  if (mergeArrow.TargetSceneUnit != null)
                  {
                     targetUnit = mergeArrow.TargetSceneUnit;
                     // When we finish the jump to the other scene, we will continue merging with the action this arrow points to.
                     NextTargetUnitOnReturn.Push(mergeArrow.TargetUnit);
                  }
                  else
                     // It's a local merge arrow. Merge the action it points to.
                     // It should be impossible for it to have no target. Let it crash if that's the case.
                     targetUnit = mergeArrow.TargetUnit;
                  // Call this routine again recursively. It will append the target's text and examine the target's arrows.
                  Accumulate(targetUnit);
                  break;
               case ReactionArrow reactionArrow:
                  gotAReactionArrow = true;
                  double highestScore = 0;
                  var reactionText = EvaluateText(reactionArrow.Code);
                  // There's a little trickiness with links here...
                  if (reactionText.Length > 0 && reactionText[0] == '{')
                  {
                     // If it's in braces, it refers to a hyperlink already in the text. Don't make a new hyperlink for it. Just take off the braces. When the user clicks on the link, it won't have braces.
                     reactionText = reactionText.Substring(1);
                     var end = reactionText.IndexOf("}");
                     if (end != -1)
                        reactionText = reactionText.Substring(0, end);
                     // -1 tells the UI to not put embedded hyperlinks on the list on the screen.
                     highestScore = -1;
                  }
                  else
                  {
                     // Sort by scores.
                     var highestScoreIdPlusSpace = "";
                     reactionArrow.Code.Traverse((code, originalSourceText) =>
                     {
                        if (!(code is ScoreCode scoreCode))
                           return true;
                        foreach (var id in scoreCode.Ids)
                        {
                           ScoreSetting scoreSetting;
                           if (Settings.TryGetValue(id, out Setting setting))
                              scoreSetting = setting as ScoreSetting;
                           else
                           {
                              scoreSetting = new ScoreSetting();
                              Settings.Add(id, scoreSetting);
                           }

                           var value = scoreSetting.ScoreValue;
                           if (value > highestScore)
                           {
                              highestScore = value;
                              highestScoreIdPlusSpace = id + " ";
                           }
                        }
                        return true;
                     });
                     if (Game.DebugMode)
                        reactionText =
                           Game.PositiveDebugTextStart +
                           highestScoreIdPlusSpace +
                           ((int)(highestScore * 100)).ToString() +
                           "% " +
                           Game.DebugTextStop +
                           reactionText;
                  }
                  accumulatedReactions[reactionText] = new ScoredReactionArrow(highestScore, reactionArrow);
                  reactionScoreDisambiguator += 0.00001;
                  break;
            }
         }
      }

      private bool EvaluateConditions(
         IEnumerable<Expression> expressions,
         out string outTrace,
         string originalSourceText)
      {
         outTrace = "";
         foreach (var expression in expressions)
         {
            if (!Settings.TryGetValue(expression.LeftId, out Setting leftSetting))
               throw new InvalidOperationException(String.Format($"Referenced undefined setting {expression.LeftId} in\n{originalSourceText}."));

            bool succeeded = false;
            if (expression.RightId == null)
            {
               // This is the 'left' or 'not left' case.
               if (!(leftSetting is AbstractBooleanSetting leftBooleanSetting))
                  throw new InvalidOperationException(String.Format($"Setting {expression.LeftId} must be a truth value in\n{originalSourceText}."));
               succeeded = leftBooleanSetting.Value != expression.Not;
               if (Game.DebugMode)
                  outTrace +=
                     "@" +
                     (succeeded ? Game.PositiveDebugTextStart : Game.NegativeDebugTextStart) +
                     "? " +
                     (expression.Not ? "not " : "") +
                     expression.LeftId +
                     " <" +
                     (leftBooleanSetting.Value ? "true" : "false") +
                     ">" +
                     Game.DebugTextStop;
            }
            else
            {
               // This is the 'left=right' or 'not left=right' case. The right ID isn't looked up like the left one is. It's a constant string to compare to. You can't compare the values of two IDs. You can't compare two booleans.
               if (!(leftSetting is StringSetting leftStringSetting))
                  throw new InvalidOperationException(String.Format($"Setting {expression.LeftId} must be a string in\n{originalSourceText}."));
               succeeded = (leftStringSetting.Value == expression.RightId) != expression.Not;
               if (Game.DebugMode)
                  outTrace +=
                     "@" +
                     (succeeded ? Game.PositiveDebugTextStart : Game.NegativeDebugTextStart) +
                     "? " +
                     (expression.Not ? "not " : "") +
                     expression.LeftId +
                     " <" +
                     leftStringSetting.Value +
                     "> = " +
                     expression.RightId +
                     Game.DebugTextStop;
            }
            if (!succeeded)
               return false;
         }
         return true;
      }

      private (bool, bool) EvaluateWhen(
         Code topCode,
         out string outTrace)
      {
         string trace = "";
         // When there are no 'when' directives, it always succeeds.
         var allSucceeded = true;
         var hadWhen = false;
         topCode.Traverse((code, originalSourceText) =>
         {
            if (code is WhenCode whenCode)
            {
               hadWhen = true;
               if (!EvaluateConditions(whenCode.GetExpressions(), out trace, originalSourceText))
                  allSucceeded = false;
            }
            return true;
         });
         outTrace = trace;
         return (allSucceeded, hadWhen);
      }

      public string DereferenceString(
         string id)
      {
         if (!Settings.TryGetValue(id, out Setting setting))
            throw new InvalidOperationException(String.Format($"Reference to undefined setting {id}."));
         if (!(setting is StringSetting stringSetting))
            throw new InvalidOperationException(String.Format($"Setting {id} must be a string."));
         return stringSetting.Value;
      }

      private string GetSpecialText(
         string specialId,
         string originalSourceText)
      {
         if (specialId == "John" || specialId == "Jane")
            return DereferenceString("jane");
         else if (specialId == "Smith")
            return DereferenceString("smith");
         else
         {
            bool heroIsMale = Settings.ContainsKey("male");
            if (specialId == "he" || specialId == "she")
               return heroIsMale ? "he" : "she";
            else if (specialId == "He" || specialId == "She")
               return heroIsMale ? "He" : "She";
            else if (specialId == "him" || specialId == "her")
               return heroIsMale ? "him" : "her";
            else if (specialId == "Him" || specialId == "Her")
               return heroIsMale ? "Him" : "Her";
            else if (specialId == "his" || specialId == "hers")
               return heroIsMale ? "his" : "hers";
            else if (specialId == "His" || specialId == "Hers")
               return heroIsMale ? "His" : "Hers";
            else if (specialId == "himself" || specialId == "herself")
               return heroIsMale ? "himself" : "herself";
            else if (specialId == "Himself" || specialId == "Herself")
               return heroIsMale ? "Himself" : "Herself";
            else if (specialId == "man" || specialId == "woman")
               return heroIsMale ? "man" : "woman";
            else if (specialId == "Man" || specialId == "Woman")
               return heroIsMale ? "Man" : "Woman";
            else if (specialId == "boy" || specialId == "girl")
               return heroIsMale ? "boy" : "girl";
            else if (specialId == "Boy" || specialId == "Girl")
               return heroIsMale ? "Boy" : "Girl";
            else if (specialId == "Mr" || specialId == "Ms")
               return heroIsMale ? "Mr." : "Ms";
            else if (specialId == "Mrs")
               return heroIsMale ? "Mr." : "Mrs.";
            else
               throw new InvalidOperationException(string.Format($"Unknown special ID '{specialId}' in\n{originalSourceText}"));
         }
      }

      private string NormalizeText(
         string text)
      {
         // Remove sequences of more than one space within text, plus remove all leading and trailing spaces. This ensures that strings with no information are zero-length.
         string fixedText = "";
         // true: skip leading spaces too.
         bool hadSpace = true;
         foreach (char letter in text)
         {
            if (letter == ' ' || letter == '\t' || letter == '\n')
            {
               if (!hadSpace)
               {
                  hadSpace = true;
                  fixedText += ' ';
               }
            }
            else
            {
               hadSpace = false;
               fixedText += letter;
            }
         }
         // Get rid of trailing spaces too.
         return fixedText.Trim();
      }

      private string EvaluateText(
         Code value)
      {
         string accumulator = "";
         value.Traverse((code, originalSourceText) =>
         {
            switch (code)
            {
               case CharacterCode characterCode:
                  accumulator += characterCode.Characters;
                  break;
               case IfCode ifCode:
                  return EvaluateConditions(ifCode.GetExpressions(), out var trace, originalSourceText);
               case SpecialCode specialCode:
                  accumulator += GetSpecialText(specialCode.Id, originalSourceText);
                  break;
            }
            return true;
         });
         // Always returns an empty string if there is no useful text.
         return NormalizeText(accumulator);
      }

      private bool EvaluateWhenElse(
         Code topCode)
      {
         bool result = false;
         topCode.Traverse((code, originalSourceText) =>
         {
            if (code is WhenElseCode whenElseCode)
               result = true;
            return true;
         });
         return result;
      }

      private void EvaluateSettingsAndScores(
         Code topCode,
         out string outTrace)
      {
         string trace = "";
         topCode.Traverse((code, originalSourceText) =>
         {
            switch (code)
            {
               case SetCode setCode:
                  foreach (var expression in setCode.GetExpressions())
                  {
                     if (expression.RightId == null)
                        // This is [set tvOn] or [set not tvOn].
                        Settings[expression.LeftId] = new BooleanSetting(!expression.Not);
                     else
                        Settings[expression.LeftId] = new StringSetting(expression.RightId);
                     if (Game.DebugMode)
                        trace += "@" + Game.PositiveDebugTextStart + "set " + (expression.Not ? "not " : "") + expression.LeftId + (expression.RightId != null ? "=" + expression.RightId : "") + Game.DebugTextStop;
                  }
                  break;
               case TextCode textCode:
                  Settings[textCode.Id] = new StringSetting(textCode.Text);
                  if (Game.DebugMode)
                     trace += "@" + Game.PositiveDebugTextStart + "text " + textCode.Id + "=" + textCode.Text + Game.DebugTextStop;
                  break;
               case ScoreCode scoreCode:
                  if (!scoreCode.SortOnly)
                     foreach (var id in scoreCode.Ids)
                     {
                        var scoreSetting = Settings[id] as ScoreSetting;
                        scoreSetting.RaiseChosenCount();
                        // Better raise this too, otherwise you could have more choices than opportunities to choose, i.e greater than 100% score.
                        scoreSetting.RaiseOpportunityCount();
                        if (Game.DebugMode)
                           trace += String.Format($"@{Game.PositiveDebugTextStart}<{id} → {scoreSetting.RatioString()} {scoreSetting.PercentString()} {(scoreSetting.Value ? "true" : "false")}>{Game.DebugTextStop}");
                     }
                  break;
            }
            return true;
         });
         outTrace = trace;
      }

      private string FixPlus(
         string text)
      {
         // We always put in a space when we concatenate different text parts, but sometimes you don't want that, so you can put in a plus sign to stop that. Ex. "hello" joined with "there" => "hello there", but "hello+" joined with "there" => "hellothere".  Useful for things like 'He said "+' joined with "'I am a fish."' => 'He said "I am a fish."'
         string fixedText = "";
         bool addSpaces = true;
         foreach (char letter in text)
         {
            if (letter == '+')
               addSpaces = false;
            else if (letter == ' ')
            {
               if (addSpaces)
                  fixedText += ' ';
            }
            else
            {
               fixedText += letter;
               addSpaces = true;
            }
         }
         return fixedText;
      }
   }
}
