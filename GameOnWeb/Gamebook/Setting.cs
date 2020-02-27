using System;

namespace Gamebook
{
   public abstract class Setting
   {
   }

   public class StringSetting: Setting
   {
      public string Value { get; private set; } = "";

      private StringSetting() { }
      public StringSetting(
         string value)
      {
         Value = value;
      }
   }

   public abstract class AbstractBooleanSetting: Setting
   {
      public abstract bool Value { get; protected set; }
   }

   public class BooleanSetting: AbstractBooleanSetting
   {
      public override bool Value { get; protected set; } = false;

      private BooleanSetting() { }
      public BooleanSetting(
         bool value)
      {
         Value = value;
      }
   }

   public class ScoreSetting: AbstractBooleanSetting
   {
      // The game sets this.
      public static double Average { get; set; }

      public override bool Value
      {
         // You are brave (for example) if your brave score is greater than average..
         get => ScoreValue > Average;
         protected set { }
      }

      public double ScoreValue
      {
         // It's '< 2' for two reasons: a) If the opportunity count is 0, it will be a divide by zero error. b) An opportunity count of 1 is very little data to judge by, but a score of 1 out of 1 = 100%, which is the best score possible! Throw away the score in that case.
         get => OpportunityCount < 2 ? 0 : (double)ChosenCount / OpportunityCount;
         protected set { }
      }

      public string PercentString() => String.Format($"{ScoreValue * 100:0}%");

      public string RatioString() => String.Format($"{ChosenCount}/{OpportunityCount}");

      private int ChosenCount;
      private int OpportunityCount;

      public int GetChosenCount() => ChosenCount;
      public int GetOpportunityCount() => OpportunityCount;

      public void RaiseChosenCount()
      {
         ++ChosenCount;
      }

      public void RaiseOpportunityCount()
      {
         ++OpportunityCount;
      }

      public ScoreSetting()
      {
         ChosenCount = 0;
         OpportunityCount = 0;
      }

      public ScoreSetting(
         int chosenCount,
         int opportunityCount)
      {
         ChosenCount = chosenCount;
         OpportunityCount = opportunityCount;
      }
   }
}
