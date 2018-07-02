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
      Sitecore.Support.Data.Managers.LanguageManager.Initialize();
    }
  }
}
