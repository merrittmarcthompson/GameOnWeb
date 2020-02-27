using System;
using System.Collections.Generic;

namespace Gamebook
{
   public class TokenType
   {
      public string Name { get; set; }

      public TokenType(
        string name)
      {
         Name = name;
      }

      public override string ToString()
      {
         return Name;
      }

      public static readonly TokenType EndOfSourceText = new TokenType("end of source text");

      public static readonly TokenType Id = new TokenType("an identifier");
      public static readonly TokenType Special = new TokenType("a special identifier");
      public static readonly TokenType Characters = new TokenType("characters");

      public static readonly TokenType End = new TokenType("'end'");
      public static readonly TokenType Else = new TokenType("'else'");
      public static readonly TokenType If = new TokenType("'if'");
      public static readonly TokenType Not = new TokenType("'not'");
      public static readonly TokenType Merge = new TokenType("'merge'");
      public static readonly TokenType Or = new TokenType("'or'");
      public static readonly TokenType Return = new TokenType("'return'");
      public static readonly TokenType Scene = new TokenType("'scene'");
      public static readonly TokenType Set = new TokenType("'set'");
      public static readonly TokenType Score = new TokenType("'score'");
      public static readonly TokenType Sort = new TokenType("'sort'");
      public static readonly TokenType Text = new TokenType("'text'");
      public static readonly TokenType When = new TokenType("'when'");

      public static readonly TokenType Comma = new TokenType("a comma");
      public static readonly TokenType Equal = new TokenType("an equal sign");
      public static readonly TokenType Period = new TokenType("a period");
   }

   public class Token
   {
      public TokenType Type { get; private set; }
      public string Value { get; private set; }
      public int LineNumber { get; private set; }

      private Token() { }
      private Token(
        TokenType type,
        string value,
        int lineNumber)
      {
         Type = type;
         Value = value;
         LineNumber = lineNumber;
      }

      public override string ToString()
      {
         string result = Type.Name;
         if (Type == TokenType.Id || Type == TokenType.Characters)
            result += " '" + Value + "'";
         return result;
      }

      public static List<Token> Tokenize(
         string sourceText,
         string sourceNameForErrorMessages)
      {
         char pushedLetter = '\0';
         char gottenLetter;
         int letterIndex;
         var specialIds = new List<string> { "John", "Jane", "Smith", "he", "He", "him", "Him", "his", "His", "himself", "Himself", "man", "Man", "boy", "Boy", "Mr", "Mrs", "she", "She", "her", "Her", "hers", "Hers", "herself", "Herself", "woman", "Woman", "girl", "Girl", "Ms" };

         // Some local helper functions

         bool GetLetter()
         {
            if (pushedLetter != '\0')
            {
               gottenLetter = pushedLetter;
               pushedLetter = '\0';
               return true;
            }

            if (letterIndex >= sourceText.Length)
            {
               gottenLetter = '\0';
               return false;
            }

            gottenLetter = sourceText[letterIndex];
            ++letterIndex;
            return true;
         }

         void UngetLetter()
         {
            pushedLetter = gottenLetter;
         }

         bool GetComment()
         {
            // Nesting example:
            //   [[ They always were.[p] ]]
            // No-nesting example:
            //   [[ They always were. ]]
            int level = 0;
            while (true)
            {
               if (!GetLetter())
                  return false;
               if (gottenLetter == '[')
                  ++level;
               else if (gottenLetter == ']')
               {
                  --level;
                  if (level < 0)
                  {
                     if (!GetLetter())
                        return false;
                     if (gottenLetter == ']')
                        return true;
                     UngetLetter();
                  }
               }
            }
         }

         // Start here

         List<Token> result = new List<Token>();
         string textAccumulator = "";
         int lineNumber = 1;

         letterIndex = 0;

         // This outer loop is for accumulating text strings
         while (true)
         {
            if (!GetLetter())
            {
               if (textAccumulator != "")
               {
                  result.Add(new Token(TokenType.Characters, textAccumulator, lineNumber));
                  textAccumulator = "";
               }
               result.Add(new Token(TokenType.EndOfSourceText, "", lineNumber));
               return result;
            }

            switch (gottenLetter)
            {
               case '\n':
                  // @ lets you explicitly put in a paragraph break. We'll clean up any extra spaces later. This lets you break contiguous text up into multiple lines within 'if' groups without having it affect formatting.
                  textAccumulator += "\n";
                  ++lineNumber;
                  break;

               case '\r':
                  // Ignore the CR in CRLF.
                  break;

               case '\t':
                  textAccumulator += "\t";
                  break;

               // Didn't want to put this way at the bottom--gets lost.
               default:
                  textAccumulator += gottenLetter;
                  break;

               case '[':
                  if (!GetLetter())
                  {
                     result.Add(new Token(TokenType.EndOfSourceText, "", lineNumber));
                     return result;
                  }

                  // Check for a [[ comment starter.
                  if (gottenLetter != '[')
                     // No comment--pretend this never happened.
                     UngetLetter();
                  else
                  {
                     //  [[ This is an example comment. ]]
                     if (!GetComment())
                     {
                        // If it returns false, it means you've reached the end.
                        result.Add(new Token(TokenType.EndOfSourceText, "", lineNumber));
                        return result;
                     }
                     // We got a text mode comment, so we break back out to the outer loop.
                     break; // switch
                  }

                  // If we have accumulated a text string, record it as the next token before going into code mode.
                  if (textAccumulator != "")
                  {
                     result.Add(new Token(TokenType.Characters, textAccumulator, lineNumber));
                     textAccumulator = "";
                  }

                  // This inner loop is for breaking out control codes
                  for (bool gotClosingBracket = false; !gotClosingBracket;)
                  {
                     if (!GetLetter())
                     {
                        result.Add(new Token(TokenType.EndOfSourceText, "", lineNumber));
                        return result;
                     }

                     switch (gottenLetter)
                     {
                        case ' ':
                        case '\r':
                           break;

                        case '\n':
                           ++lineNumber;
                           break;

                        case ']':
                           // Leave code mode 
                           gotClosingBracket = true;
                           break;

                        case '=':
                           result.Add(new Token(TokenType.Equal, "=", lineNumber));
                           break;

                        case '.':
                           result.Add(new Token(TokenType.Period, ".", lineNumber));
                           break;

                        case ',':
                           result.Add(new Token(TokenType.Comma, ",", lineNumber));
                           break;

                        case '[':
                           // Must be a comment.
                           if (!GetLetter())
                           {
                              result.Add(new Token(TokenType.EndOfSourceText, "", lineNumber));
                              return result;
                           }
                           if (gottenLetter != '[')
                              throw new InvalidOperationException(string.Format($"file {sourceNameForErrorMessages} line {lineNumber}: expected '[[' but got '[{gottenLetter}' in\n{sourceText}"));

                           if (!GetComment())
                           {
                              // If it returns false, it means you've reached the end.
                              result.Add(new Token(TokenType.EndOfSourceText, "", lineNumber));
                              return result;
                           }
                           // Done skipping comment. Move on to the next token.
                           break;

                        default:
                           if (!Char.IsLetterOrDigit(gottenLetter) || gottenLetter == '_')
                              throw new InvalidOperationException(string.Format($"file {sourceNameForErrorMessages} line {lineNumber}: unexpected character '{gottenLetter}' in\n{sourceText}"));

                           string id = "";
                           do
                           {
                              id += gottenLetter;

                              if (!GetLetter())
                                 break;
                           } while (Char.IsLetterOrDigit(gottenLetter) || gottenLetter == '_' || gottenLetter == '.');

                           UngetLetter();
                           if (id == "if")
                              result.Add(new Token(TokenType.If, id, lineNumber));
                           else if (id == "else")
                              result.Add(new Token(TokenType.Else, id, lineNumber));
                           else if (id == "or")
                              result.Add(new Token(TokenType.Or, id, lineNumber));
                           else if (id == "not")
                              result.Add(new Token(TokenType.Not, id, lineNumber));
                           else if (id == "end")
                              result.Add(new Token(TokenType.End, id, lineNumber));
                           else if (id == "when")
                              result.Add(new Token(TokenType.When, id, lineNumber));
                           else if (id == "set")
                              result.Add(new Token(TokenType.Set, id, lineNumber));
                           else if (id == "score")
                              result.Add(new Token(TokenType.Score, id, lineNumber));
                           else if (id == "sort")
                              result.Add(new Token(TokenType.Sort, id, lineNumber));
                           else if (id == "text")
                              result.Add(new Token(TokenType.Text, id, lineNumber));
                           else if (id == "merge")
                              result.Add(new Token(TokenType.Merge, id, lineNumber));
                           else if (id == "return")
                              result.Add(new Token(TokenType.Return, id, lineNumber));
                           else if (id == "scene")
                              result.Add(new Token(TokenType.Scene, id, lineNumber));
                           else if (specialIds.Contains(id))
                              result.Add(new Token(TokenType.Special, id, lineNumber));
                           else
                              result.Add(new Token(TokenType.Id, id, lineNumber));
                           break;
                     }
                  }
                  break;
            }
         }
      }
   }
}
