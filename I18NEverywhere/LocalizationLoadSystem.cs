using System;
using Colossal.PSI.Common;
using Colossal.Serialization.Entities;
using Game;
using Game.PSI;
using Game.SceneFlow;
using Game.UI;

namespace I18NEverywhere;

public partial class LocalizationLoadSystem : GameSystemBase
{
    private Guid? Updater { get; set; }

    protected override void OnUpdate()
    {
        // do nothing
    }

    protected override void OnCreate()
    {
        base.OnCreate();
        Updater = GameManager.instance.RegisterUpdater(Init);
    }

    private bool Init()
    {
        if (!GameManager.instance.modManager.isInitialized ||
            GameManager.instance.gameMode != GameMode.MainMenu ||
            GameManager.instance.state == GameManager.State.Loading ||
            GameManager.instance.state == GameManager.State.Booting
           ) return false;

        var localeId = GameManager.instance.localizationManager.activeLocaleId;
        var fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
        I18NEverywhere.Logger.Info("Init Load.");
        I18NEverywhere.Logger.Info($"{nameof(localeId)}: {localeId}");
        I18NEverywhere.Logger.Info($"{nameof(fallbackLocaleId)}: {fallbackLocaleId}");

        if (!I18NEverywhere.LoadLocales(localeId, fallbackLocaleId))
        {
            I18NEverywhere.Logger.Error("Cannot load locales.");
        }
        else
        {
            I18NEverywhere.Instance.InvokeEvent();
        }

        return true;
    }

    protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
    {
        base.OnGameLoadingComplete(purpose, mode);

        if (mode == GameMode.MainMenu)
        {
            if (I18NEverywhere.Instance.GameLoaded)
            {
                return;
            }

            NotificationSystem.Pop("i18n-load", delay: 10f,
                titleId: "I18NEverywhere",
                textId: "I18NEverywhere.Detail",
                progressState: ProgressState.Complete,
                progress: 100);
            I18NEverywhere.Instance.GameLoaded = true;
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (Updater is null)
        {
            return;
        }

        GameManager.instance.UnregisterUpdater(Updater.Value);
    }
}