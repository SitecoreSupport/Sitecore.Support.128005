namespace Sitecore.Support.Pipelines.InitializeManagers
{
  using Sitecore;
  using Sitecore.Data.Managers;
  using Sitecore.Pipelines;
  using System;

  [UsedImplicitly]
  public class InitializeLanguageManager
  {
    [UsedImplicitly]
    public void Process(PipelineArgs args)
    {
      Sitecore.Data.Managers.LanguageManager.Initialize();
      #region Added code
      Sitecore.Support.Data.Managers.LanguageManager.Initialize();
      #endregion
    }
  }
}
