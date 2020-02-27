using System;
using System.Collections.Generic;

namespace Gamebook
{
   // These classes make up the Code data structure.

   public abstract class Code
   {
      // Traverse is the routine that allows other modules to execute the code.
      public void Traverse(
        Func<Code, string, bool> examine)
      {
         (this as SequenceCode).StartTraverse(examine);
      }

      public abstract void Traverse(
        Func<Code, string, bool> examine,
        string originalSourceText);

      // This makes Code objects appear in the debugger.
      public abstract override string ToString();

      public static Code Compile(
        string sourceCode,
        string sourceNameForErrorMessages)
      {
         // Compile the text to a code sequence.
         // Change ' to ’ right away. This both looks better and keeps it from interfering with Javascript quotes in HTML.
         // TODO: But what about double quotes? They must get converted just before display, because you can't tell whether to use left or right double quotes until the whole text is assembled. But that makes links with quotes in them not work.
         var tokens = Token.Tokenize(sourceCode.Replace('\'', '’'), sourceNameForErrorMessages);
         return SequenceCode.BuildFromTokens(tokens, sourceCode, sourceNameForErrorMessages);
      }
   }

   public class SequenceCode: Code
   {
      // This is a sequence of code operations. The Traverse function is the only way to get to the Codes.
      private List<Code> Codes { get; set; } = new List<Code>();
      private string SourceText = null;

      public void StartTraverse(
        Func<Code, string, bool> examine)
      {
         Traverse(examine, SourceText);
      }

      public override void Traverse(
        Func<Code, string, bool> examine,
        string originalSourceText)
      {
         foreach (var code in Codes)
            code.Traverse(examine, originalSourceText);
      }

      public override string ToString()
      {
         string result = "";
         foreach (var code in Codes)
         {
            string objectString = code.ToString();
            if (objectString.Length > 16)
               objectString = objectString.Substring(0, 16) + "... ";
         }
         return result;
      }

      private class LookAhead
      {
         private List<Token> Tokens;
         private int TokenIndex = 0;
         public string Value { get; private set; }
         public TokenType Type { get; private set; }

         public LookAhead(
            List<Token> tokens)
         {
            Tokens = tokens;
         }

         public bool Got(
            TokenType type)
         {
            // This should never go off the end. There is already an end of source text marker at the end of the tokens.
            var token = Tokens[TokenIndex++];
            if (token.Type == type)
            {
               Value = token.Value;
               Type = type;
               return true;
            }
            --TokenIndex;
            return false;
         }

         public void Require(
            TokenType expected,
            string sourceTextForErrorMessages,
            string sourceNameForErrorMessages)
         {
            // This should never go off the end. There is already an end of source text marker at the end of the tokens.
            var actual = Tokens[TokenIndex++];
            if (actual.Type != expected)
               throw new InvalidOperationException(string.Format($"file {sourceNameForErrorMessages} line {actual.LineNumber}: expected {expected} but got '{actual.Value}' in\n{sourceTextForErrorMessages}"));
            Value = actual.Value;
            Type = expected;
         }
      }

      // All sequence codes come from BuildFromTokens.
      private SequenceCode() { }

      // This allows base Code class to construct a sequence.
      public static SequenceCode BuildFromTokens(
         List<Token> tokens,
         string sourceTextForErrorMessages,
         string sourceNameForErrorMessages)
      {
         LookAhead Look = new LookAhead(tokens);
         var sequenceCode = GetSequence();
         Look.Require(TokenType.EndOfSourceText, sourceTextForErrorMessages, sourceNameForErrorMessages);
         sequenceCode.SourceText = sourceTextForErrorMessages;
         return sequenceCode;

         // Some local helper functions.

         SequenceCode GetSequence()
         {
            var result = new SequenceCode();

            while (true)
            {
               if (Look.Got(TokenType.Characters))
                  result.Codes.Add(new CharacterCode(Look.Value));
               else if (Look.Got(TokenType.Special))
                  // Ex. [he]
                  result.Codes.Add(new SpecialCode(Look.Value));
               else if (Look.Got(TokenType.Merge))
               {
                  // [merge]
                  // [merge sceneId]
                  string sceneId = null;
                  if (Look.Got(TokenType.Id))
                     sceneId = Look.Value;
                  result.Codes.Add(new MergeCode(sceneId));
               }
               else if (Look.Got(TokenType.Return))
               {
                  // [return]
                  result.Codes.Add(new ReturnCode());
               }
               else if (Look.Got(TokenType.Scene))
               {
                  // [scene soundsLikeAScam]
                  Look.Require(TokenType.Id, sourceTextForErrorMessages, sourceNameForErrorMessages);
                  result.Codes.Add(new SceneCode(Look.Value));
               }
               else if (Look.Got(TokenType.Score) || Look.Got(TokenType.Sort))
               {
                  // SCORE ID [, ID...]
                  // SORT ID [, ID...]
                  var sortOnly = Look.Type == TokenType.Sort;
                  List<string> ids = new List<string>();
                  do
                  {
                     Look.Require(TokenType.Id, sourceTextForErrorMessages, sourceNameForErrorMessages);
                     ids.Add(Look.Value);
                  } while (Look.Got(TokenType.Comma));
                  result.Codes.Add(new ScoreCode(ids, sortOnly));
               }
               else if (Look.Got(TokenType.Text))
               {
                  Look.Require(TokenType.Id, sourceTextForErrorMessages, sourceNameForErrorMessages);
                  string id = Look.Value;
                  string text = "";
                  if (Look.Got(TokenType.Characters))
                     text = Look.Value;
                  Look.Require(TokenType.End, sourceTextForErrorMessages, sourceNameForErrorMessages);
                  result.Codes.Add(new TextCode(id, text));
               }
               else if (Look.Got(TokenType.Set))
                  result.Codes.Add(new SetCode(GetExpressions(false)));
               else if (Look.Got(TokenType.When))
               {
                  if (Look.Got(TokenType.Else))
                     result.Codes.Add(new WhenElseCode());
                  else
                     result.Codes.Add(new WhenCode(GetExpressions(true)));
               }
               else if (Look.Got(TokenType.If))
               {
                  var ifCode = GetIf();
                  result.Codes.Add(ifCode);

                  // The whole if/or case statement is terminated by 'end'.
                  Look.Require(TokenType.End, sourceTextForErrorMessages, sourceNameForErrorMessages);
               }
               else
               {
                  // Hopefully the token we've been looking at is something the caller is expecting to see next (i.e. end of source text).
                  return result;
               }
            }
         }

         List<Expression> GetExpressions(
            bool allowNotEqual)
         {
            // ID
            // NOT ID
            // ID=ID
            // NOT ID=ID
            // allowNotEqual: [when not a=b] makes sense. But [set not a=b] doesn't mean anything.
            var result = new List<Expression>();
            do
            {
               var not = Look.Got(TokenType.Not);
               Look.Require(TokenType.Id, sourceTextForErrorMessages, sourceNameForErrorMessages);
               var leftId = Look.Value;
               string rightId = null;
               if (allowNotEqual || !not)
               {
                  if (Look.Got(TokenType.Equal))
                  {
                     Look.Require(TokenType.Id, sourceTextForErrorMessages, sourceNameForErrorMessages);
                     rightId = Look.Value;
                  }
               }
               result.Add(new Expression(not, leftId, rightId));
            } while (Look.Got(TokenType.Comma));
            return result;
         }

         /* This flat 'if' sequence with 'or' (which is like 'elif' but more Englishy)...

           [if reaction=Flee]
             A
           [or reaction=Escape]
             B
           [else]
             C
           [end]

          ...is equivalent to this nested sequence:

           if reaction=Flee
             A
           else
             if reaction=Escape
               B
             else
               C
         */
         IfCode GetIf()
         {
            // This is called after getting 'if' or 'or'.
            // First get the expression. It's like one of these:
            //   [if brave]
            //   [if not killedInspector]
            var expressions = GetExpressions(true);
            var trueCode = GetSequence();
            Code falseCode = null;

            if (Look.Got(TokenType.Else))
               falseCode = GetSequence();
            else if (Look.Got(TokenType.Or))
               falseCode = GetIf();
            // Otherwise must be 'end'. Let caller handle it.
            return new IfCode(expressions, trueCode, falseCode);
         }
      }
   }

   public class IfCode: Code
   {
      private List<Expression> Expressions;
      public IEnumerable<Expression> GetExpressions()
      {
         return Expressions;
      }
      public Code TrueCode { get; private set; }
      public Code FalseCode { get; private set; }

      private IfCode() { }
      public IfCode(
         List<Expression> expressions,
         Code trueCode,
         Code falseCode)
      {
         Expressions = expressions;
         TrueCode = trueCode;
         FalseCode = falseCode;
      }

      public override void Traverse(
        Func<Code, string, bool> examine,
        string originalSourceText)
      {
         if (examine(this, originalSourceText))
            TrueCode.Traverse(examine, originalSourceText);
         else if (FalseCode != null)
            FalseCode.Traverse(examine, originalSourceText);
      }

      public override string ToString()
      {
         return "if " + Expressions.ToString();
      }
   }

   public class WhenCode: Code
   {
      private List<Expression> Expressions;
      public IEnumerable<Expression> GetExpressions()
      {
         return Expressions;
      }

      private WhenCode() { }
      public WhenCode(
         List<Expression> expressions)
      {
         Expressions = expressions;
      }

      public override void Traverse(
        Func<Code, string, bool> examine,
        string originalSourceText)
      {
         examine(this, originalSourceText);
      }

      public override string ToString()
      {
         return "when " + Expressions.ToString();
      }
   }

   public class WhenElseCode: Code
   {
      public WhenElseCode() { }

      public override void Traverse(
        Func<Code, string, bool> examine,
        string originalSourceText)
      {
         examine(this, originalSourceText);
      }

      public override string ToString()
      {
         return "when else";
      }
   }

   public class SetCode: Code
   {
      private List<Expression> Expressions;
      public IEnumerable<Expression> GetExpressions()
      {
         return Expressions;
      }

      private SetCode() { }
      public SetCode(
         List<Expression> expressions)
      {
         Expressions = expressions;
      }

      public override void Traverse(
         Func<Code, string, bool> examine,
         string originalSourceText)
      {
         examine(this, originalSourceText);
      }

      public override string ToString()
      {
         return "set" + Expressions.ToString();
      }
   }

   public class ScoreCode: Code
   {
      public List<string> Ids { get; private set; }
      public bool SortOnly { get; private set; }

      private ScoreCode() { }
      public ScoreCode(
         List<string> ids,
         bool sortOnly)
      {
         Ids = ids;
         SortOnly = sortOnly;
      }

      public override void Traverse(
         Func<Code, string, bool> examine,
         string originalSourceText)
      {
         examine(this, originalSourceText);
      }

      public override string ToString()
      {
         return "score " + Ids.ToString();
      }
   }

   public class TextCode: Code
   {
      public string Id { get; private set; }
      public string Text { get; private set; }

      private TextCode() { }
      public TextCode(
         string id,
         string text)
      {
         Id = id;
         Text = text;
      }

      public override void Traverse(
         Func<Code, string, bool> examine,
         string originalSourceText)
      {
         examine(this, originalSourceText);
      }

      public override string ToString()
      {
         return Text.ToString();
      }
   }

   public class CharacterCode: Code
   {
      public string Characters { get; private set; }

      private CharacterCode() { }
      public CharacterCode(
        string characters)
      {
         Characters = characters;
      }

      public override void Traverse(
         Func<Code, string, bool> examine,
         string originalSourceText)
      {
         examine(this, originalSourceText);
      }
      public override string ToString()
      {
         return Characters;
      }
   }

   public class MergeCode: Code
   {
      public string SceneId { get; private set; }

      private MergeCode() { }
      public MergeCode(
         string sceneId)
      {
         SceneId = sceneId;
      }

      public override void Traverse(
         Func<Code, string, bool> examine,
         string originalSourceText)
      {
         examine(this, originalSourceText);
      }

      public override string ToString()
      {
         return "merge " + SceneId;
      }
   }

   public class ReturnCode: Code
   {
      public ReturnCode() { }

      public override void Traverse(
         Func<Code, string, bool> examine,
         string originalSourceText)
      {
         examine(this, originalSourceText);
      }

      public override string ToString()
      {
         return "return";
      }
   }

   public class SceneCode: Code
   {
      public string SceneId { get; private set; }

      private SceneCode() { }
      public SceneCode(
         string sceneId)
      {
         SceneId = sceneId;
      }

      public override void Traverse(
         Func<Code, string, bool> examine,
         string originalSourceText)
      {
         examine(this, originalSourceText);
      }

      public override string ToString()
      {
         return "scene " + SceneId;
      }
   }

   public class SpecialCode: Code
   {
      public string Id { get; private set; }

      private SpecialCode() { }
      public SpecialCode(
        string id)
      {
         Id = id;
      }

      public override void Traverse(
         Func<Code, string, bool> examine,
         string originalSourceText)
      {
         examine(this, originalSourceText);
      }

      public override string ToString()
      {
         return "[" + Id + "]";
      }
   }

   public class Expression
   {
      // This represents:
      //    tvOn
      //    not tvOn
      //    tvOn=mr_rogers
      //    not tvOn=mr_rogers
      public string LeftId { get; private set; }
      public string RightId { get; private set; }
      public bool Not { get; private set; }

      private Expression() { }
      public Expression(
         bool not,
         string leftId,
         string rightId)
      {
         Not = not;
         LeftId = leftId;
         RightId = rightId;
      }

      public override string ToString()
      {
         string result = "";
         if (Not)
            result += "not ";
         result += LeftId;
         if (RightId != null)
            result += "=" + RightId;
         return result;
      }
   }
}
