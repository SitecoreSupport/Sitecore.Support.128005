namespace Sitecore.Support.Data.Managers
{
  using Microsoft.Extensions.DependencyInjection;
  using Sitecore;
  using Sitecore.Abstractions;
  using Sitecore.Caching;
  using Sitecore.Collections;
  using Sitecore.Configuration;
  using Sitecore.Configuration.KnownSettings;
  using Sitecore.Data;
  using Sitecore.Data.Engines;
  using Sitecore.Data.Engines.DataCommands;
  using Sitecore.Data.Events;
  using Sitecore.Data.Items;
  using Sitecore.DependencyInjection;
  using Sitecore.Diagnostics;
  using Sitecore.Globalization;
  using Sitecore.SecurityModel;
  using Sitecore.StringExtensions;
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.Linq;
  using System.Runtime.CompilerServices;
  using System.Runtime.InteropServices;
  using System.Threading;

  public class LanguageProvider
  {
    private HashSet<string> _registeredLanguages;
    protected static readonly string LanguagesCacheName = "LanguageProvider - Languages";
    protected readonly object RegisteredLanguagesSyncRoot;
    protected static readonly string[] WellKnownIllegalLanguageNames = new string[] { "-", "default", "__language" };

    [Obsolete("Use constructor overload with all dependencies.")]
    public LanguageProvider() : this(ServiceProviderServiceExtensions.GetRequiredService<BaseCacheManager>(ServiceLocator.ServiceProvider), ServiceProviderServiceExtensions.GetRequiredService<BaseSettings>(ServiceLocator.ServiceProvider))
    {
    }

    [Obsolete("Please use another constructor with parameters")]
    public LanguageProvider(BaseCacheManager cacheManager, BaseSettings settings) : this(ServiceProviderServiceExtensions.GetRequiredService<BaseItemManager>(ServiceLocator.ServiceProvider), cacheManager, ServiceProviderServiceExtensions.GetRequiredService<BaseFactory>(ServiceLocator.ServiceProvider), ServiceProviderServiceExtensions.GetRequiredService<BaseLog>(ServiceLocator.ServiceProvider), settings.Caching().SmallCacheSize, true, true, settings.Languages().AutoRemoveItemData)
    {
    }

    protected LanguageProvider(BaseItemManager itemManager, BaseCacheManager cacheManager, BaseFactory factory, BaseLog log, long languageCacheSize, bool registerNewInstance, bool configurationSet, bool autoRenameItemData)
    {
      this.RegisteredLanguagesSyncRoot = new object();
      Assert.ArgumentNotNull(itemManager, "itemManager");
      Assert.ArgumentNotNull(cacheManager, "cacheManager");
      Assert.ArgumentNotNull(factory, "factory");
      Assert.ArgumentNotNull(log, "log");
      this.ItemManager = itemManager;
      this.CacheManager = cacheManager;
      this.Factory = factory;
      this.Log = log;
      this.ConfigurationSet = configurationSet;
      this.AutoRenameItemData = autoRenameItemData;
      this.LanguageCache = this.CacheManager.GetNamedInstance(LanguagesCacheName, languageCacheSize, registerNewInstance);
      this.InitializeEventHandlers();
    }

    [Obsolete("Deprecated.")]
    public virtual void AddLanguage(Language language, Database database)
    {
    }

    private void AddLanguagesToCache(LanguageCollection languages, Database database)
    {
      this.LanguageCache.Add(database.Name, languages);
    }

    private void AttachToDatabase(Database database)
    {
      if ((database.Engines != null) && (database.Engines.DataEngine != null))
      {
        DataEngine dataEngine = database.Engines.DataEngine;
        dataEngine.CreatedItem += new EventHandler<ExecutedEventArgs<CreateItemCommand>>(this.DataEngine_CreatedItem);
        dataEngine.CreatedItemRemote += new EventHandler<ItemCreatedRemoteEventArgs>(this.DataEngine_CreatedItemRemote);
        dataEngine.DeletedItem += new EventHandler<ExecutedEventArgs<DeleteItemCommand>>(this.DataEngine_DeletedItem);
        dataEngine.DeletedItemRemote += new EventHandler<ItemDeletedRemoteEventArgs>(this.DataEngine_DeletedItemRemote);
        dataEngine.SavingItem += new EventHandler<ExecutingEventArgs<SaveItemCommand>>(this.DataEngine_SavingItem);
        dataEngine.SavedItem += new EventHandler<ExecutedEventArgs<SaveItemCommand>>(this.DataEngine_SavedItem);
        dataEngine.SavedItemRemote += new EventHandler<ItemSavedRemoteEventArgs>(this.DataEngine_SavedItemRemote);
      }
    }

    private void DataEngine_CreatedItem(object sender, ExecutedEventArgs<CreateItemCommand> e)
    {
      Item result = e.Command.Result;
      this.HandleItemCreated(result);
    }

    private void DataEngine_CreatedItemRemote(object sender, ItemCreatedRemoteEventArgs e)
    {
      this.HandleItemCreated(e.Item);
    }

    private void DataEngine_DeletedItem(object sender, ExecutedEventArgs<DeleteItemCommand> e)
    {
      Item item = e.Command.Item;
      this.HandleItemDeleted(item, true);
    }

    private void DataEngine_DeletedItemRemote(object sender, ItemDeletedRemoteEventArgs e)
    {
      this.HandleItemDeleted(e.Item, false);
    }

    private void DataEngine_SavedItem(object sender, ExecutedEventArgs<SaveItemCommand> e)
    {
      Item item = e.Command.Item;
      ItemChanges changes = e.Command.Changes;
      this.HandleItemSaved(item, changes, true);
    }

    private void DataEngine_SavedItemRemote(object sender, ItemSavedRemoteEventArgs e)
    {
      Item item = e.Item;
      ItemChanges changes = e.Changes;
      this.HandleItemSaved(item, changes, false);
    }

    private void DataEngine_SavingItem(object sender, ExecutingEventArgs<SaveItemCommand> e)
    {
      Item item = e.Command.Item;
      if (e.Command.Changes.Renamed && (item.TemplateID == TemplateIDs.Language))
      {
        bool condition = LanguageManager.IsValidLanguageName(item.Name);
        if (!condition)
        {
          Item parent = this.ItemManager.GetParent(item, SecurityCheck.Disable);
          condition = (parent != null) && ((((parent.TemplateID == TemplateIDs.Template) || (parent.ID == ItemIDs.BranchesRoot)) || (parent.TemplateID == TemplateIDs.BranchTemplate)) || (parent.TemplateID == TemplateIDs.BranchTemplateFolder));
        }
        object[] args = new object[] { item.Name };
        Assert.IsTrue(condition, "Item can not be renamed to '{0}' as it is not a valid language name.", args);
      }
    }

    private CultureAndRegionInfoBuilder GetCultureBuilder(string languageName, bool assert = true)
    {
      string[] strArray = StringUtil.Divide(languageName, '-', true);
      if (assert)
      {
        object[] args = new object[] { languageName };
        Assert.IsTrue(strArray.Length >= 2, "The custom language name '{0}' is invalid. A custom language name must be on the form: isoLanguageCode-isoRegionCode-customName. The language codes are two-letter ISO 639-1, and the regions codes are are two-letter ISO 3166. Also, customName must not exceed 8 characters in length. Valid example: en-US-East. For the full list of requirements, see: http://msdn2.microsoft.com/en-US/library/system.globalization.cultureandregioninfobuilder.cultureandregioninfobuilder.aspx", args);
      }
      if (strArray.Length < 2)
      {
        return null;
      }
      string cultureName = strArray[0].Trim();
      CultureAndRegionInfoBuilder builder = new CultureAndRegionInfoBuilder(languageName, CultureAndRegionModifiers.None);
      CultureInfo cultureInfo = Language.GetCultureInfo(cultureName);
      //The fix: use EnglishName instead of NativeName
      if (cultureInfo.EnglishName.ToLowerInvariant().Contains("unknown language"))
      {
        return null;
      }
      builder.LoadDataFromCultureInfo(cultureInfo);
      builder.LoadDataFromRegionInfo(new RegionInfo(cultureInfo.LCID));
      return builder;
    }

    private DataSource GetDataSource(Database database) =>
        database.DataManager.DataSource;

    public virtual Language GetLanguage(string name)
    {
      Language language;
      Assert.ArgumentNotNull(name, "name");
      if (!Language.TryParse(name, out language))
      {
        return null;
      }
      return language;
    }

    public Language GetLanguage(string name, Database database)
    {
      LanguageCollection source = this.GetLanguages(database);
      if (source == null)
      {
        return null;
      }
      return source.FirstOrDefault<Language>(language => language.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public virtual Item GetLanguageItem(Language language, Database database)
    {
      ID languageItemId = this.GetLanguageItemId(language, database);
      if (ID.IsNullOrEmpty(languageItemId))
      {
        object[] parameters = new object[] { TemplateIDs.Language, language.Name };
        return database.SelectSingleItem("fast://*[@@templateid = '{0}' and @@name = '{1}']".FormatWith(parameters));
      }
      return database.GetItem(languageItemId);
    }

    public virtual ID GetLanguageItemId(Language language, Database database) =>
        (from languageFromDatabase in this.GetLanguages(database)
         where languageFromDatabase == language
         select languageFromDatabase.Origin.ItemId).FirstOrDefault<ID>();

    public virtual LanguageCollection GetLanguages(Database database)
    {
      Assert.ArgumentNotNull(database, "database");
      LanguageCollection languagesFromCache = this.GetLanguagesFromCache(database);
      if (languagesFromCache == null)
      {
        languagesFromCache = this.GetLanguagesFromDatabase(database);
        this.AddLanguagesToCache(languagesFromCache, database);
      }
      return languagesFromCache;
    }

    private LanguageCollection GetLanguagesFromCache(Database database) =>
        (this.LanguageCache[database.Name] as LanguageCollection);

    private LanguageCollection GetLanguagesFromDatabase(Database database) =>
        this.GetDataSource(database).GetLanguages();

    private void HandleItemCreated(Item item)
    {
      if (item.TemplateID == TemplateIDs.Language)
      {
        this.InvalidateCaches(item.Database);
      }
    }

    private void HandleItemDeleted(Item item, bool removeLanguageData)
    {
      if ((item.TemplateID == TemplateIDs.Language) && Settings.Languages.AutoRemoveItemData)
      {
        Language language = this.GetLanguage(item.Name);
        if (language != null)
        {
          if (removeLanguageData)
          {
            this.RemoveLanguageData(language, item.Database);
          }
          this.RemoveLanguagesFromCache(item.Database);
          this.InvalidateCaches(item.Database);
        }
      }
    }

    private void HandleItemSaved(Item item, ItemChanges changes, bool updateLanguageBindings)
    {
      if (changes.Renamed && (item.TemplateID == TemplateIDs.Language))
      {
        if (updateLanguageBindings && this.AutoRenameItemData)
        {
          string name = item.InnerData.Definition.Name;
          string str2 = item.Name;
          if ((str2 != name) && (this.GetLanguage(str2) != null))
          {
            this.RenameLanguageData(name, str2, item.Database);
          }
        }
        this.RemoveLanguagesFromCache(item.Database);
      }
    }

    private void InitializeEventHandlers()
    {
      if (this.ConfigurationSet)
      {
        foreach (Database database in this.Factory.GetDatabases())
        {
          this.AttachToDatabase(database);
          database.Constructed += (sender, args) => this.AttachToDatabase(args.Database);
        }
      }
    }

    public virtual void InvalidateCaches(Database database)
    {
      Assert.ArgumentNotNull(database, "database");
      this.CacheManager.ClearAllCaches();
    }

    protected virtual bool IsKnownToBeInvalid(string candidateLanguageName) =>
        WellKnownIllegalLanguageNames.Any<string>(knownInvalidName => candidateLanguageName.Equals(knownInvalidName, StringComparison.OrdinalIgnoreCase));

    public virtual bool IsLanguageNameDefined(Database database, string languageName)
    {
      Assert.ArgumentNotNull(database, "database");
      Assert.ArgumentNotNull(languageName, "name");
      if (string.IsNullOrEmpty(languageName))
      {
        return false;
      }
      LanguageCollection languages = this.GetLanguages(database);
      if ((languages != null) && (languages.Count != 0))
      {
        return languages.Contains(languageName);
      }
      return true;
    }

    public virtual bool IsValidLanguageName(string name)
    {
      Assert.ArgumentNotNull(name, "name");
      if (this.IsKnownToBeInvalid(name))
      {
        return false;
      }
      if (this.LanguageRegistered(name))
      {
        return true;
      }
      try
      {
        return (this.GetCultureBuilder(name, false) != null);
      }
      catch
      {
        return false;
      }
    }

    public virtual bool LanguageRegistered(string name)
    {
      Assert.ArgumentNotNull(name, "name");
      return this.RegisteredLanguages.Contains(name);
    }

    public virtual bool LanguageRegistered(string name, Database database)
    {
      Assert.ArgumentNotNull(name, "name");
      Assert.ArgumentNotNull(database, "database");
      LanguageCollection source = this.GetLanguages(database);
      if (source == null)
      {
        return false;
      }
      return source.Any<Language>(language => language.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    protected virtual void MarkLanguageAsRegistered(string name)
    {
      ISet<string> registeredLanguages = this.RegisteredLanguages;
      object registeredLanguagesSyncRoot = this.RegisteredLanguagesSyncRoot;
      lock (registeredLanguagesSyncRoot)
      {
        registeredLanguages.Add(name);
      }
    }

    public virtual bool RegisterLanguage(string name)
    {
      Assert.ArgumentNotNullOrEmpty(name, "name");
      if (name[0] != '_')
      {
        if (this.LanguageRegistered(name))
        {
          return true;
        }
        try
        {
          this.GetCultureBuilder(name, true).Register();
          this.MarkLanguageAsRegistered(name);
          this.Log.Info("Custom language registered: " + name, this);
          return true;
        }
        catch (Exception exception)
        {
          this.Log.Error("Attempt to register language failed. Language: " + name, exception, this);
          this.Log.Error("A custom language name must be on the form: isoLanguageCode-isoRegionCode-customName. The language codes are two-letter ISO 639-1, and the regions codes are are two-letter ISO 3166. Also, customName must not exceed 8 characters in length. Valid example: en-US-East. For the full list of requirements, see: http://msdn2.microsoft.com/en-US/library/system.globalization.cultureandregioninfobuilder.cultureandregioninfobuilder.aspx", this);
        }
        this.Log.Info("Attempting temporary registration. This might work, but with worse performance than a real registration.", this);
        try
        {
          CultureInfo info = Language.CreateCultureInfo(name);
          this.MarkLanguageAsRegistered(name);
          this.Log.Info("Temporary registration succeeded. Culture: " + info.Name, this);
          return true;
        }
        catch
        {
        }
      }
      return false;
    }

    public virtual void RemoveLanguageData(Language language, Database database)
    {
      this.GetDataSource(database).RemoveLanguageData(language);
      this.InvalidateCaches(database);
    }

    private void RemoveLanguagesFromCache(Database database)
    {
      Assert.ArgumentNotNull(database, "database");
      this.LanguageCache.Remove(database.Name);
    }

    public virtual void RenameLanguageData(string fromLanguage, string toLanguage, Database database)
    {
      this.GetDataSource(database).RenameLanguageData(fromLanguage, toLanguage);
      this.InvalidateCaches(database);
    }

    protected bool AutoRenameItemData { get; private set; }

    protected BaseCacheManager CacheManager { get; private set; }

    protected bool ConfigurationSet { get; private set; }

    protected BaseFactory Factory { get; private set; }

    protected BaseItemManager ItemManager { get; private set; }

    protected ICache LanguageCache { get; private set; }

    protected BaseLog Log { get; private set; }

    protected virtual ISet<string> RegisteredLanguages
    {
      get
      {
        if (this._registeredLanguages == null)
        {
          object registeredLanguagesSyncRoot = this.RegisteredLanguagesSyncRoot;
          lock (registeredLanguagesSyncRoot)
          {
            if (Volatile.Read<HashSet<string>>(ref this._registeredLanguages) != null)
            {
              return this._registeredLanguages;
            }
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CultureInfo info in Language.GetCultures(CultureTypes.AllCultures))
            {
              set.Add(info.Name);
            }
            Volatile.Write<HashSet<string>>(ref this._registeredLanguages, set);
          }
        }
        return this._registeredLanguages;
      }
    }


  }
}
