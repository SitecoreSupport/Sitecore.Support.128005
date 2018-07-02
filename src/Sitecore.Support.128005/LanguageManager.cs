namespace Sitecore.Data.Managers
{
  using Sitecore.Abstractions;
  using Sitecore.Collections;
  using Sitecore.Data;
  using Sitecore.Data.Items;
  using Sitecore.DependencyInjection;
  using Sitecore.Globalization;
  using System;

  public sealed class LanguageManager
  {
    private static readonly LazyResetable<BaseLanguageManager> Instance = ServiceLocator.GetRequiredResetableService<BaseLanguageManager>();
    private static readonly LazyResetable<Sitecore.Data.Managers.LanguageProvider> LanguageProvider = ServiceLocator.GetRequiredResetableService<Sitecore.Data.Managers.LanguageProvider>();

    public static Language GetLanguage(string name) =>
        Instance.Value.GetLanguage(name);

    public static Language GetLanguage(string name, Database database) =>
        Instance.Value.GetLanguage(name, database);

    public static Item GetLanguageItem(Language language, Database database) =>
        Instance.Value.GetLanguageItem(language, database);

    public static ID GetLanguageItemId(Language language, Database database) =>
        Instance.Value.GetLanguageItemId(language, database);

    public static LanguageCollection GetLanguages(Database database) =>
        Instance.Value.GetLanguages(database);

    public static void Initialize()
    {
      Instance.Value.Initialize();
    }

    public static bool IsLanguageNameDefined(Database database, string languageName) =>
        Instance.Value.IsLanguageNameDefined(database, languageName);

    public static bool IsValidLanguageName(string name) =>
        Instance.Value.IsValidLanguageName(name);

    public static bool LanguageRegistered(string name) =>
        Instance.Value.LanguageRegistered(name);

    public static bool LanguageRegistered(string name, Database database) =>
        Instance.Value.LanguageRegistered(name, database);

    public static bool RegisterLanguage(string name) =>
        Instance.Value.RegisterLanguage(name);

    public static void RemoveLanguageData(Language language, Database database)
    {
      Instance.Value.RemoveLanguageData(language, database);
    }

    public static void RenameLanguageData(string fromLanguage, string toLanguage, Database database)
    {
      Instance.Value.RenameLanguageData(fromLanguage, toLanguage, database);
    }

    public static Language DefaultLanguage =>
        Instance.Value.GetDefaultLanguage();
  }
}
