using System.Windows.Media.Animation;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using Microsoft.Win32;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MacroDoButler
{
    public partial class MainWindow : Window
    {
        private readonly PresetService presetService;
        private readonly InputService inputService;
        private readonly PresetBackupService backupService;
        private readonly PresetTransferService transferService;
        private readonly PresetOrganizationService organizationService = new PresetOrganizationService();
        private readonly HotkeyStorageService hotkeyStorage;
        private MacroSettingsStorageService macroSettingsStorage;
        private HotkeyService hotkeyService;
        private HotkeySettings hotkeySettings;
        private string capturingAction;
        private string capturedHotkey;
        private string hotkeyRegistrationWarning;
        private DispatcherTimer hotkeyMessageTimer;
        private TextBlock hotkeyMessageTarget;
        private string hotkeyMessageText;
        private Preset selectedPreset;
        private MovementSettings liveEditMovementSettings;
        private bool updatingSelectors;
        private readonly string[] selectorEmptyText = { "Nenhum jogo cadastrado", "Nenhum preset neste jogo" };
        private long movementRevision;
        private volatile bool enabled;
        private volatile bool secondaryActive;
        private UpdateService updateService;
        private UpdatePreferencesStorageService updatePreferencesStorage;
        private UpdatePreferences updatePreferences;
        private CancellationTokenSource updateCancellation;
        private DispatcherTimer updateCooldownTimer;
        private bool updateCooldownFromRateLimit;
        private bool loadingUpdatePreferences;
        private string updateDownloadPath;
        private static readonly TimeSpan SuccessfulUpdateCheckCooldown=TimeSpan.FromMinutes(2);

        public MainWindow()
        {
            string dataDirectory = UserDataService.Initialize();
            InitializeComponent();
            AdvancedPage.TestWeaponSelected=secondary=> { secondaryActive=secondary; movementRevision++; UpdateWeaponStatus(); };
            hotkeyStorage = new HotkeyStorageService(dataDirectory);
            string hotkeyWarning; hotkeySettings=hotkeyStorage.Load(out hotkeyWarning);
            macroSettingsStorage = new MacroSettingsStorageService(dataDirectory);
            IntervalSlider.Value=macroSettingsStorage.LoadInterval();
            SecondaryIntervalSlider.Value=IntervalSlider.Value;
            IntervalText.Text=((int)IntervalSlider.Value).ToString();
            SecondaryIntervalText.Text=((int)SecondaryIntervalSlider.Value).ToString();
            presetService = new PresetService(dataDirectory);
            updateService=new UpdateService(GetApplicationVersion(),Process.GetCurrentProcess().MainModule.FileName,dataDirectory);
            updatePreferencesStorage=new UpdatePreferencesStorageService(dataDirectory);updatePreferences=updatePreferencesStorage.Load();
            backupService = new PresetBackupService(presetService.FilePath);
            transferService = new PresetTransferService(backupService);
            inputService = new InputService(GetMovementSettings);
            Loaded += delegate { RefreshHotkeyUi(); ShowHotkeyWarnings(hotkeyWarning); InitializeUpdateUi(); ImportPendingPresets(dataDirectory); };

            IList<Preset> initialPresets = presetService.LoadAll();
            RefreshOrganization(initialPresets.FirstOrDefault());
            if (initialPresets.Count == 0) { PrimaryVerticalSlider.Value=4; PrimaryHorizontalSlider.Value=0; SecondaryVerticalSlider.Value=0; SecondaryHorizontalSlider.Value=0; RequireRight.IsChecked=true; }
            UpdateStatus();
            UpdateWeaponStatus();
            Loaded += LoadGameArtwork;
            inputService.Start();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            hotkeyService=new HotkeyService(new WindowInteropHelper(this).Handle);
            hotkeyService.ActionRequested+=HotkeyService_ActionRequested;
            string error=hotkeyService.Register(hotkeySettings);
            hotkeyRegistrationWarning=error;
            if(error!=null) Dispatcher.BeginInvoke(new Action(delegate { ShowHotkeyWarnings(null); }));
        }

        private void HotkeyService_ActionRequested(string action)
        {
            if(!Dispatcher.CheckAccess()){if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action<string>(HotkeyService_ActionRequested),action);return;}
            if(action=="ToggleMacro") Toggle(); else if(action=="SwitchWeapon") ToggleWeapon(); else if(action=="ExitApplication") Close(); else if(action=="ShowWindow") ToggleWindowVisibility();
        }

        private void ToggleWindowVisibility()
        {
            if(WindowState==WindowState.Minimized)
            {
                Show(); WindowState=WindowState.Normal; Activate();
            }
            else WindowState=WindowState.Minimized;
        }

        private sealed class HotkeyRow
        {
            public string Action { get; set; } public string Label { get; set; } public string Description { get; set; } public string Value { get; set; }
        }
        private void RefreshHotkeyUi()
        {
            if(HotkeyItems==null)return;
            var rows=new[]{new HotkeyRow{Action="ToggleMacro",Label="Ligar/desligar macro",Description="Controla o estado ativo do macro",Value=hotkeySettings.ToggleMacro},new HotkeyRow{Action="SwitchWeapon",Label="Alternar arma",Description="Alterna entre primária e secundária",Value=hotkeySettings.SwitchWeapon},new HotkeyRow{Action="ExitApplication",Label="Fechar aplicativo",Description="Encerra o Velqora",Value=hotkeySettings.ExitApplication},new HotkeyRow{Action="ShowWindow",Label="Mostrar janela",Description="Minimiza e restaura a interface",Value=hotkeySettings.ShowWindow}};
            HotkeyItems.ItemsSource=rows;
            HotkeySummary.Text=hotkeySettings.ToggleMacro+" liga/desliga  •  "+hotkeySettings.SwitchWeapon+" alterna  •  "+hotkeySettings.ExitApplication+" encerra";
        }
        private bool? settingsSingleColumn;
        private void SettingsCards_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            bool singleColumn = SettingsCards.ActualWidth < 820;
            if (settingsSingleColumn == singleColumn) return;
            settingsSingleColumn = singleColumn;
            var cards = SettingsLeftColumn.Children.Cast<FrameworkElement>()
                .Concat(SettingsRightColumn.Children.Cast<FrameworkElement>())
                .OrderBy(card => int.Parse((string)card.Tag)).ToList();
            SettingsLeftColumn.Children.Clear();
            SettingsRightColumn.Children.Clear();
            SettingsCards.ColumnDefinitions[1].Width = singleColumn ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            foreach (var card in cards)
                (singleColumn || int.Parse((string)card.Tag) < 2 ? SettingsLeftColumn : SettingsRightColumn).Children.Add(card);
        }

        private void ChangeHotkey_Click(object sender,RoutedEventArgs e)
        {
            var button=sender as Button; var row=button==null?null:button.DataContext as HotkeyRow; if(row==null)return;
            capturingAction=row.Action; capturedHotkey=null; HotkeyCaptureStatus.Text="Pressione uma tecla ou botão do mouse para "+row.Label+". ENTER confirma, ESC cancela."; Focus(); Keyboard.Focus(this);
        }
        private void Window_PreviewKeyDown(object sender,KeyEventArgs e)
        {
            if(capturingAction==null)return; e.Handled=true; Key key;
            if(!HotkeyParser.TryKeyFromEvent(e,out key))return;
            if(key==Key.Escape){CancelCapture();return;}
            if(key==Key.Enter){if(!string.IsNullOrEmpty(capturedHotkey))ApplyCapturedHotkey();else HotkeyCaptureStatus.Text="Pressione uma tecla ou botão do mouse.";return;}
            if(key==Key.Back||key==Key.Delete){capturedHotkey=null;HotkeyCaptureStatus.Text="Atalho desativado; ENTER confirma.";return;}
            if(!HotkeyValidator.IsAllowed(key)){HotkeyCaptureStatus.Text="Essa tecla não pode ser usada como atalho.";return;}
            capturedHotkey=HotkeyParser.Format(HotkeyParser.CurrentModifiers(),key); HotkeyCaptureStatus.Text=capturedHotkey+" — pressione ENTER para confirmar ou continue capturando.";
        }
        private void Window_PreviewMouseDown(object sender,MouseButtonEventArgs e)
        {
            if(capturingAction==null)return;
            string mouseButton;
            if(!HotkeyParser.TryMouseButtonFromEvent(e,out mouseButton))return;
            e.Handled=true;
            capturedHotkey=HotkeyParser.Format(HotkeyParser.CurrentModifiers(),mouseButton);
            HotkeyCaptureStatus.Text=capturedHotkey+" — pressione ENTER para confirmar ou continue capturando.";
        }
        private void CancelCapture(){capturingAction=null;capturedHotkey=null;HotkeyCaptureStatus.Text="Captura cancelada.";Keyboard.ClearFocus();}
        private void ApplyCapturedHotkey()
        {
            HotkeySettings candidate=hotkeySettings.Copy(); SetHotkeyValue(candidate,capturingAction,capturedHotkey);
            string validation=HotkeyValidator.Validate(candidate); if(validation!=null){HotkeyCaptureStatus.Text=validation;return;}
            string conflict=FindHotkeyConflict(candidate,capturingAction); if(conflict!=null){if(MessageBox.Show(this,"Esse atalho já é usado por “"+conflict+"”. Deseja substituir o atalho anterior?","Conflito de atalhos",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return; SetHotkeyValue(candidate,conflict,null);}
            string error=hotkeyService==null?"A janela ainda não está pronta.":hotkeyService.TryReplace(hotkeySettings,candidate);
            if(error!=null){hotkeyRegistrationWarning=error;ShowHotkeyWarnings(null);HotkeyCaptureStatus.Text=error;return;}
            try{hotkeyStorage.Save(candidate);}catch(Exception ex){string rollback=hotkeyService.TryReplace(candidate,hotkeySettings);HotkeyCaptureStatus.Text="Não foi possível salvar hotkeys.json: "+ex.Message+(rollback==null?"":" "+rollback);return;}
            hotkeySettings=candidate; capturingAction=null; capturedHotkey=null; RefreshHotkeyUi(); HotkeyCaptureStatus.Text="Atalho atualizado com sucesso.";
            hotkeyRegistrationWarning=hotkeyService.LastRegistrationWarning;
            ShowHotkeyWarnings(null);
            if(string.IsNullOrEmpty(hotkeyService.LastRegistrationWarning))ShowTransientHotkeyMessage(HotkeyCaptureStatus,"Atalho atualizado com sucesso.",Brushes.LightGreen);
        }
        private static string FindHotkeyConflict(HotkeySettings s,string action)
        {
            string value=GetHotkeyValue(s,action); HotkeyBinding target; if(!HotkeyParser.TryParse(value,out target))return null;
            string[] actions={"ToggleMacro","SwitchWeapon","ExitApplication","ShowWindow"}; string[] labels={"Ligar/desligar macro","Alternar arma","Fechar aplicativo","Mostrar janela"};
            for(int i=0;i<actions.Length;i++)if(actions[i]!=action){HotkeyBinding other;if(HotkeyParser.TryParse(GetHotkeyValue(s,actions[i]),out other)&&target.Equals(other))return labels[i];} return null;
        }
        private static string GetHotkeyValue(HotkeySettings s,string action){if(action=="ToggleMacro")return s.ToggleMacro;if(action=="SwitchWeapon")return s.SwitchWeapon;if(action=="ExitApplication")return s.ExitApplication;return s.ShowWindow;}
        private static void SetHotkeyValue(HotkeySettings s,string action,string value){if(action=="ToggleMacro")s.ToggleMacro=value;else if(action=="SwitchWeapon")s.SwitchWeapon=value;else if(action=="ExitApplication")s.ExitApplication=value;else s.ShowWindow=value;}
        private void RestoreHotkeys_Click(object sender,RoutedEventArgs e)
        {
            if(MessageBox.Show(this,"Restaurar F1, F2 e F12 e remover os atalhos opcionais?","Restaurar padrões",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            HotkeySettings defaults=HotkeySettings.Defaults(); string error=hotkeyService.TryReplace(hotkeySettings,defaults); if(error!=null){hotkeyRegistrationWarning=error;ShowHotkeyWarnings(null);return;}
            try{hotkeyStorage.Save(defaults);}catch(Exception ex){string rollback=hotkeyService.TryReplace(defaults,hotkeySettings);HotkeyStatus.Text="Não foi possível salvar: "+ex.Message+(rollback==null?"":" "+rollback);return;} hotkeySettings=defaults; RefreshHotkeyUi(); hotkeyRegistrationWarning=hotkeyService.LastRegistrationWarning; ShowHotkeyWarnings(null); ShowTransientHotkeyMessage(HotkeyCaptureStatus,"Padrões restaurados.",Brushes.LightGreen);
        }

        private void ShowHotkeyWarnings(string storageWarning)
        {
            string[] warnings={storageWarning,hotkeyRegistrationWarning};
            string message=string.Join(" ",warnings.Where(value=>!string.IsNullOrWhiteSpace(value)));
            HotkeyStatus.Text=message;
            HotkeyStatus.Foreground=string.IsNullOrEmpty(message)?(Brush)FindResource("MutedBrush"):Brushes.Orange;
        }

        private void ShowTransientHotkeyMessage(TextBlock target,string message,Brush color)
        {
            if(hotkeyMessageTimer==null)
            {
                hotkeyMessageTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2.5) };
                hotkeyMessageTimer.Tick+=delegate
                {
                    hotkeyMessageTimer.Stop();
                    if(hotkeyMessageTarget!=null&&hotkeyMessageTarget.Text==hotkeyMessageText)hotkeyMessageTarget.Text=string.Empty;
                    hotkeyMessageTarget=null; hotkeyMessageText=null;
                };
            }
            hotkeyMessageTimer.Stop(); hotkeyMessageTarget=target; hotkeyMessageText=message; target.Text=message; target.Foreground=color; hotkeyMessageTimer.Start();
        }

        private MovementSettings GetMovementSettings()
        {
            MovementSettings preview = Volatile.Read(ref liveEditMovementSettings);
            if (preview != null) return preview;
            if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(new Func<MovementSettings>(GetMovementSettings));
            if(AdvancedPage.Visibility==Visibility.Visible)
            {
                MovementSettings advancedPreview=AdvancedPage.GetLiveSettings(enabled,secondaryActive);
                if(advancedPreview!=null) { advancedPreview.Revision+=movementRevision; return advancedPreview; }
            }
            return new MovementSettings
            {
                Revision = movementRevision,
                Trajectory = selectedPreset == null ? null : (secondaryActive ? selectedPreset.SecondaryTrajectory : selectedPreset.PrimaryTrajectory),
                Enabled = enabled && !(AdvancedPage.Visibility==Visibility.Visible && AdvancedPage.IsDrawingOpen),
                RequireRightButton = RequireRight.IsChecked == true,
                HorizontalPixelsPerSecond = ForceUnits.ToPixelsInt(secondaryActive ? SecondaryHorizontalSlider.Value : PrimaryHorizontalSlider.Value,true),
                VerticalPixelsPerSecond = ForceUnits.ToPixelsInt(secondaryActive ? SecondaryVerticalSlider.Value : PrimaryVerticalSlider.Value),
                IntervalMilliseconds = Math.Max(2, (int)(secondaryActive ? SecondaryIntervalSlider.Value : IntervalSlider.Value))
            };
        }

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            string name = PresetName.Text.Trim();
            if (name.Length == 0) { ShowFeedback("Digite um nome para o preset.", false); return; }
            try
            {
                string game=GameSelector.SelectedItem as string;
                if(string.IsNullOrWhiteSpace(game)){ShowFeedback("Crie ou selecione um jogo antes de salvar.",false);return;}
                Preset existing=presetService.LoadAll().FirstOrDefault(p=>p.GameName.Equals(game,StringComparison.OrdinalIgnoreCase)&&p.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
                bool samePreset=selectedPreset!=null&&PresetService.IdentityEquals(selectedPreset,new Preset{GameName=game,Name=name});
                if(existing!=null&&!samePreset){ShowFeedback("Já existe um preset com esse nome neste jogo. Escolha outro nome ou selecione o existente.",false);return;}
                Preset preset = selectedPreset == null ? new Preset { GameName=game, CharacterName="Preset", WeaponName="Arma não definida" } : ClonePreset(selectedPreset);
                preset.GameName=game; preset.CharacterName="Preset"; preset.WeaponName="Arma não definida"; preset.Name = name;
                preset.PrimaryVertical = ForceUnits.ToPixelsInt(PrimaryVerticalSlider.Value);
                preset.PrimaryHorizontal = ForceUnits.ToPixelsInt(PrimaryHorizontalSlider.Value,true);
                preset.SecondaryVertical = ForceUnits.ToPixelsInt(SecondaryVerticalSlider.Value);
                preset.SecondaryHorizontal = ForceUnits.ToPixelsInt(SecondaryHorizontalSlider.Value,true);
                preset.IntervalMilliseconds = (int)IntervalSlider.Value;
                preset.SecondaryIntervalMilliseconds = (int)SecondaryIntervalSlider.Value;
                preset.RequireRightButton = RequireRight.IsChecked == true;
                backupService.CreateBackup();
                organizationService.Save(presetService, preset);
                movementRevision++;
                selectedPreset = preset;
                RefreshOrganization(preset);
                ShowFeedback("Preset salvo com sucesso.", true);
            }
            catch (Exception exception) { ShowFeedback("Erro ao salvar: " + exception.Message, false); }
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            Preset target = selectedPreset;
            if (target == null) { ShowFeedback("Selecione um preset para excluir.", false); return; }
            string fullName = target.GameName + " • " + target.Name;
            if (System.Windows.MessageBox.Show(this, "Deseja excluir o preset '" + fullName + "'?", "Confirmar exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                backupService.CreateBackup();
                presetService.Delete(target);
                bool deleteEmptyGame=false;
                bool gameHasPresets=presetService.LoadAll().Any(p=>p.GameName.Equals(target.GameName,StringComparison.OrdinalIgnoreCase));
                if(!gameHasPresets&&!PresetService.IsUncategorized(target.GameName))
                    deleteEmptyGame=System.Windows.MessageBox.Show(this,"Este era o último preset do jogo “"+target.GameName+"”. Deseja excluir o jogo também?","Jogo sem presets",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
                if(deleteEmptyGame)presetService.DeleteGame(target.GameName);
                RefreshOrganization(presetService.LoadAll().FirstOrDefault(p=>p.GameName.Equals(target.GameName,StringComparison.OrdinalIgnoreCase)),target.GameName);
                ShowFeedback(deleteEmptyGame?"Preset e jogo excluídos. Backup criado.":"Preset excluído. Backup criado.", true);
            }
            catch (Exception exception) { ShowFeedback("Erro ao excluir: " + exception.Message, false); }
        }

        private void ExportPresets_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Title = "Exportar presets JSON", Filter = "Arquivo de presets JSON (*.json)|*.json", DefaultExt = ".json", AddExtension = true, FileName = "presets.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { transferService.Export(dialog.FileName, presetService.LoadAll()); ShowTransferFeedback("Presets exportados com sucesso.", true); }
            catch (Exception exception) { ShowTransferFeedback("Erro ao exportar: " + exception.Message, false); }
        }

        private void ImportPresets_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Importar presets", Filter = "JSON ou CFG (*.json;*.cfg)|*.json;*.cfg|Todos os arquivos (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                transferService.ReadAndValidate(dialog.FileName);
                var choice = new DuplicateChoiceWindow(this);
                if (choice.ShowDialog() != true) return;
                backupService.CreateBackup();
                ImportResult result = transferService.Import(dialog.FileName, presetService, selectedPreset == null ? null : selectedPreset.Name, choice.Mode);
                RefreshOrganization(selectedPreset == null ? presetService.LoadAll().FirstOrDefault() : presetService.LoadAll().FirstOrDefault(p => PresetService.IdentityEquals(p, selectedPreset)));
                ShowTransferFeedback("Importados: " + result.Imported + " · Substituídos: " + result.Replaced + " · Ignorados: " + result.Ignored, true);
            }
            catch (Exception exception) { ShowTransferFeedback("Importação cancelada: " + exception.Message, false); }
        }

        private void ImportPendingPresets(string dataDirectory)
        {
            string pendingDirectory = Path.Combine(dataDirectory, "pending-imports");
            if (!Directory.Exists(pendingDirectory)) return;
            int imported = 0, ignored = 0;
            foreach (string file in Directory.GetFiles(pendingDirectory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string extension = Path.GetExtension(file);
                if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    ImportResult result = transferService.Import(file, presetService, selectedPreset == null ? null : selectedPreset.Name, DuplicatePresetMode.Ignore);
                    imported += result.Imported;
                    ignored += result.Ignored;
                    File.Delete(file);
                }
                catch (Exception exception)
                {
                    ShowTransferFeedback("Não foi possível importar " + Path.GetFileName(file) + ": " + exception.Message + ". O arquivo foi preservado em " + pendingDirectory + ".", false);
                    return;
                }
            }
            if (imported > 0 || ignored > 0)
            {
                RefreshOrganization(presetService.LoadAll().FirstOrDefault());
                ShowTransferFeedback("Presets da instalação: " + imported + " importados · " + ignored + " já existentes preservados.", true);
            }
        }

        private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName=Path.GetDirectoryName(presetService.FilePath),
                    UseShellExecute=true
                });
            }
            catch(Exception exception) { ShowTransferFeedback("Não foi possível abrir a pasta de dados: "+exception.Message,false); }
        }

        private void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            try { string backup = backupService.CreateBackup(); ShowTransferFeedback(backup == null ? "O JSON ainda não foi criado." : "Backup criado: " + backup, true); }
            catch (Exception exception) { ShowTransferFeedback("Erro ao criar backup: " + exception.Message, false); }
        }

        private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            IList<PresetBackupInfo> backups;
            try { backups = backupService.ListBackups(); }
            catch (Exception exception) { ShowTransferFeedback("Erro ao listar backups: " + exception.Message, false); return; }
            if (backups.Count == 0) { ShowTransferFeedback("Nenhum backup foi encontrado.", false); return; }
            var chooser = new BackupChooserWindow(backups, this);
            if (chooser.ShowDialog() != true || chooser.SelectedBackup == null) return;
            if (System.Windows.MessageBox.Show(this, "Restaurar o backup de " + chooser.SelectedBackup.CreatedAt.ToString("dd/MM/yyyy HH:mm:ss") + "? Um backup do estado atual será criado antes.", "Confirmar restauração", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { backupService.Restore(chooser.SelectedBackup.FilePath, presetService); RefreshOrganization(presetService.LoadAll().FirstOrDefault()); ShowTransferFeedback("Backup " + Path.GetFileName(chooser.SelectedBackup.FilePath) + " restaurado com sucesso.", true); }
            catch (Exception exception) { ShowTransferFeedback("Erro ao restaurar backup: " + exception.Message, false); }
        }

        private void RefreshOrganization(Preset selected, string selectedGame = null)
        {
            updatingSelectors = true;
            IList<string> knownGames=presetService.LoadGames();
            string game = selected == null ? selectedGame : selected.GameName;
            GameSelector.ItemsSource = knownGames;
            RefreshGameBrowser(knownGames);
            GameSelector.ToolTip = GameSelector.Items.Count == 0 ? selectorEmptyText[0] : "Jogo ativo";
            GameEmptyText.Visibility = GameSelector.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GameSelector.SelectedItem = game;
            if(GameSelector.SelectedItem==null&&knownGames.Count>0){GameSelector.SelectedIndex=0;game=GameSelector.SelectedItem as string;}
            RefreshPresetsForGame(game, selected == null ? null : selected.Name);
            Preset current=selected ?? selectedPreset;
            updatingSelectors = false;
            selectedPreset = current;
            if(current!=null) LoadPreset(current); else PresetName.Text="";
        }
        private void RefreshPresetsForGame(string game,string selectedName)
        {
            IList<Preset> presets=presetService.LoadAll().Where(p=>game!=null&&p.GameName.Equals(game,StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p.Name).ToList();
            PresetSelector.ItemsSource=presets.Select(p=>p.Name).ToList();
            PresetSelector.ToolTip=presets.Count==0?selectorEmptyText[1]:"Preset";
            PresetEmptyText.Visibility=presets.Count==0?Visibility.Visible:Visibility.Collapsed;
            PresetSelector.SelectedItem=selectedName;
            if(selectedName==null&&presets.Count>0)PresetSelector.SelectedIndex=0;
            if(PresetSelector.SelectedItem!=null){Preset p=presets.FirstOrDefault(x=>x.Name.Equals(PresetSelector.SelectedItem.ToString(),StringComparison.OrdinalIgnoreCase));if(p!=null)selectedPreset=p;}
            else selectedPreset=null;
            SyncPresetBrowser(presets, game);
        }
        private void LoadPreset(Preset preset)
        {
            movementRevision++;
            selectedPreset = preset;
            updatingSelectors = true;
            GameSelector.SelectedItem = preset.GameName;
            RefreshPresetsForGame(preset.GameName,preset.Name);
            updatingSelectors = false;
            PrimaryVerticalSlider.Value = ForceUnits.Display(Clamp(preset.PrimaryVertical, 0, 10000)); PrimaryHorizontalSlider.Value = ForceUnits.Display(Clamp(preset.PrimaryHorizontal, -5000, 5000),true);
            SecondaryVerticalSlider.Value = ForceUnits.Display(Clamp(preset.SecondaryVertical, 0, 10000)); SecondaryHorizontalSlider.Value = ForceUnits.Display(Clamp(preset.SecondaryHorizontal, -5000, 5000),true);
            IntervalSlider.Value = preset.IntervalMilliseconds >= 2 ? Clamp(preset.IntervalMilliseconds, 2, 50) : 7;
            SecondaryIntervalSlider.Value = Clamp(preset.SecondaryIntervalMilliseconds ?? (int)IntervalSlider.Value, 2, 50);
            RequireRight.IsChecked = preset.RequireRightButton;
            PresetName.Text = preset.Name; secondaryActive = preset.WeaponSlot == WeaponSlot.Secondary; UpdateWeaponStatus(); ShowFeedback("Preset carregado no macro.", true);
        }
        private void Organization_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingSelectors || presetService == null) return;
            string game=GameSelector.SelectedItem as string;
            if (sender==GameSelector) { RefreshPresetsForGame(game,null); if(selectedPreset!=null)LoadPreset(selectedPreset);else PresetName.Text=""; }
        }

        private void PresetSelector_SelectionChanged(object sender,SelectionChangedEventArgs e)
        {
            if(updatingSelectors||PresetSelector.SelectedItem==null)return;
            string game=GameSelector.SelectedItem as string,name=PresetSelector.SelectedItem.ToString();
            Preset preset=presetService.LoadAll().FirstOrDefault(p=>game!=null&&p.GameName.Equals(game,StringComparison.OrdinalIgnoreCase)&&p.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
            if(preset!=null)LoadPreset(preset);
        }

        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            var dialog=new GamePresetOrganizerWindow(this,presetService.LoadGames(),presetService.LoadAll(),GameSelector.SelectedItem as string);
            if(dialog.ShowDialog()!=true)return;
            try
            {
                backupService.CreateBackup();
                if(dialog.DeleteGameRequested)
                {
                    presetService.DeleteGame(dialog.GameName);
                    Preset remaining=presetService.LoadAll().FirstOrDefault();
                    RefreshOrganization(remaining);
                    ShowFeedback("Jogo excluído. Backup criado.",true);
                    return;
                }
                if(dialog.RenameGameRequested)presetService.RenameGame(dialog.RenameGameFrom,dialog.RenameGameTo);
                presetService.SaveGame(dialog.GameName);
                foreach(string discoveredGame in dialog.GamesToAdd)presetService.SaveGame(discoveredGame);
                int moved=0, skipped=0, transferred=0;
                foreach(Preset item in dialog.SelectedPresets)
                {
                    Preset duplicate=presetService.LoadAll().FirstOrDefault(p=>p.GameName.Equals(dialog.GameName,StringComparison.OrdinalIgnoreCase)&&p.Name.Equals(item.Name,StringComparison.OrdinalIgnoreCase));
                    if(duplicate!=null&&!PresetService.IdentityEquals(duplicate,item)){skipped++;continue;}
                    presetService.DeleteOne(item);
                    item.GameName=dialog.GameName; item.CharacterName="Preset"; item.WeaponName="Arma não definida";
                    organizationService.Save(presetService,item); moved++;
                }
                if(dialog.TransferPresetsRequested)transferred=presetService.TransferPresets(dialog.TransferSourceGame,dialog.TransferTargetGame);
                Preset selected=presetService.LoadAll().FirstOrDefault(p=>p.GameName.Equals(dialog.GameName,StringComparison.OrdinalIgnoreCase));
                if(selected==null&&selectedPreset!=null&&selectedPreset.GameName.Equals(dialog.GameName,StringComparison.OrdinalIgnoreCase))selected=selectedPreset;
                RefreshOrganization(selected,dialog.GameName);
                ShowFeedback((dialog.RenameGameRequested?"Jogo renomeado e salvo. ":"Jogo salvo. ")+"Presets adicionados: "+moved+(transferred>0?" · Transferidos: "+transferred:string.Empty)+(skipped>0?" · Ignorados por nome repetido: "+skipped:string.Empty),true);
            }
            catch(Exception ex){ShowFeedback("Erro: "+ex.Message,false);}
        }
        private void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Deixe o ComboBox aberto receber o wheel: mover o ScrollViewer principal
            // enquanto o Popup está aberto faz a lista parecer que se soltou do campo.
            if (GameSelector.IsDropDownOpen || PresetSelector.IsDropDownOpen) return;
            ScrollViewer pane=sender as ScrollViewer;
            if(pane!=null){pane.ScrollToVerticalOffset(pane.VerticalOffset-e.Delta);e.Handled=true;}
        }
        private static Preset ClonePreset(Preset p){return new Preset{PrimaryTrajectory=Trajectory.Normalize(p.PrimaryTrajectory),SecondaryTrajectory=Trajectory.Normalize(p.SecondaryTrajectory),GameName=p.GameName,CharacterName=p.CharacterName,WeaponName=p.WeaponName,WeaponSlot=p.WeaponSlot,Name=p.Name,PrimaryVertical=p.PrimaryVertical,PrimaryHorizontal=p.PrimaryHorizontal,SecondaryVertical=p.SecondaryVertical,SecondaryHorizontal=p.SecondaryHorizontal,IntervalMilliseconds=p.IntervalMilliseconds,SecondaryIntervalMilliseconds=p.SecondaryIntervalMilliseconds,RequireRightButton=p.RequireRightButton,CreatedAt=p.CreatedAt,UpdatedAt=p.UpdatedAt};}

        private bool editingNumericText;
        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Slider slider = sender as Slider;
            if (slider == null) return;
            TextBox target = null;
            switch (slider.Tag as string)
            {
                case "PrimaryVertical": target = PrimaryVerticalText; break;
                case "PrimaryHorizontal": target = PrimaryHorizontalText; break;
                case "SecondaryVertical": target = SecondaryVerticalText; break;
                case "SecondaryHorizontal": target = SecondaryHorizontalText; break;
                case "Interval": target = IntervalText; break;
                case "SecondaryInterval": target = SecondaryIntervalText; break;
            }
            if (target != null && !editingNumericText)
            {
                string tag=slider.Tag as string;
                bool horizontal=tag?.Contains("Horizontal")==true;
                target.Text=tag=="Interval"||tag=="SecondaryInterval"?((int)slider.Value).ToString():
                    ForceUnits.Format(slider.Value*ForceUnits.PixelsPerUnitFor(horizontal),horizontal);
            }
            if(IsInitialized)UpdateMacroSummary();

        }

        private void NumericText_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            int value;
            if (textBox == null || !TryReadNumericText(textBox, out value)) { SyncAllText(); return; }
            if (textBox == PrimaryVerticalText) PrimaryVerticalSlider.Value = ForceUnits.Display(Clamp(value, 0, 10000));
            else if (textBox == PrimaryHorizontalText) PrimaryHorizontalSlider.Value = ForceUnits.Display(Clamp(value, -5000, 5000),true);
            else if (textBox == SecondaryVerticalText) SecondaryVerticalSlider.Value = ForceUnits.Display(Clamp(value, 0, 10000));
            else if (textBox == SecondaryHorizontalText) SecondaryHorizontalSlider.Value = ForceUnits.Display(Clamp(value, -5000, 5000),true);
            else if (textBox == IntervalText) IntervalSlider.Value = Clamp(value, 2, 50);
            else if (textBox == SecondaryIntervalText) SecondaryIntervalSlider.Value = Clamp(value, 2, 50);
            SyncAllText();
        }

        private void NumericText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsInitialized) return;
            TextBox textBox = sender as TextBox;
            int value;
            if (textBox == null || !TryReadNumericText(textBox, out value)) return;
            editingNumericText=true;
            try
            {
                if (textBox == PrimaryVerticalText) PrimaryVerticalSlider.Value = ForceUnits.Display(Clamp(value, 0, 10000));
                else if (textBox == PrimaryHorizontalText) PrimaryHorizontalSlider.Value = ForceUnits.Display(Clamp(value, -5000, 5000),true);
                else if (textBox == SecondaryVerticalText) SecondaryVerticalSlider.Value = ForceUnits.Display(Clamp(value, 0, 10000));
                else if (textBox == SecondaryHorizontalText) SecondaryHorizontalSlider.Value = ForceUnits.Display(Clamp(value, -5000, 5000),true);
                else if (textBox == IntervalText) IntervalSlider.Value = Clamp(value, 2, 50);
                else if (textBox == SecondaryIntervalText) SecondaryIntervalSlider.Value = Clamp(value, 2, 50);
            }
            finally { editingNumericText=false; }
        }

        private bool TryReadNumericText(TextBox textBox,out int value)
        {
            if(textBox==IntervalText||textBox==SecondaryIntervalText)return int.TryParse(textBox.Text,out value);
            double number; value=0;
            bool vertical=textBox==PrimaryVerticalText||textBox==SecondaryVerticalText;
            if(!ForceUnits.TryParse(textBox.Text,out number)||number < (vertical?0:-500)||number > (vertical?100:500))return false;
            value=ForceUnits.ToPixelsInt(number,!vertical); return true;
        }
        private void SyncAllText()
        {
            PrimaryVerticalText.Text = ForceUnits.Format(PrimaryVerticalSlider.Value*ForceUnits.PixelsPerUnit);
            PrimaryHorizontalText.Text = ForceUnits.Format(PrimaryHorizontalSlider.Value*ForceUnits.PixelsPerUnitFor(true),true);
            SecondaryVerticalText.Text = ForceUnits.Format(SecondaryVerticalSlider.Value*ForceUnits.PixelsPerUnit);
            SecondaryHorizontalText.Text = ForceUnits.Format(SecondaryHorizontalSlider.Value*ForceUnits.PixelsPerUnitFor(true),true);
            IntervalText.Text = ((int)IntervalSlider.Value).ToString();
            SecondaryIntervalText.Text = ((int)SecondaryIntervalSlider.Value).ToString();
        }

        private void Toggle() { movementRevision++; enabled = !enabled; RefreshLiveEditMovementSettings(); UpdateStatus(); AudioCueService.PlayMacroToggle(); }
        private void ToggleWeapon() { movementRevision++; secondaryActive = !secondaryActive; RefreshLiveEditMovementSettings(); UpdateWeaponStatus(); AudioCueService.PlayWeaponSwitch(); }

        private void UpdateStatus()
        {
            UpdateMacroSummary();
        }

        private void UpdateMacroSummary()
        {
            if(MovementSummary!=null && PrimaryHorizontalSlider!=null && SecondaryHorizontalSlider!=null)
                MovementSummary.ShowSettings(selectedPreset,secondaryActive,enabled,
                    ForceUnits.ToPixelsInt(secondaryActive?SecondaryHorizontalSlider.Value:PrimaryHorizontalSlider.Value,true),
                    ForceUnits.ToPixelsInt(secondaryActive?SecondaryVerticalSlider.Value:PrimaryVerticalSlider.Value));
            bool primaryEnabled = enabled && !secondaryActive;
            bool secondaryEnabled = enabled && secondaryActive;
            SummaryPrimaryState.Text = primaryEnabled ? "Ativada" : "Desativada";
            SummarySecondaryState.Text = secondaryEnabled ? "Ativada" : "Desativada";
            SummaryPrimaryState.Foreground = primaryEnabled ? BrushOf("#7EE2A8") : (Brush)FindResource("MutedBrush");
            SummarySecondaryState.Foreground = secondaryEnabled ? BrushOf("#7EE2A8") : (Brush)FindResource("MutedBrush");
        }

        private void UpdateWeaponStatus()
        {
            UpdateMacroSummary();
            WeaponStatusText.Text = secondaryActive ? "Arma ativa: Secundária" : "Arma ativa: Primária";
            PrimaryCard.Opacity = secondaryActive ? 0.70 : 1;
            SecondaryCard.Opacity = secondaryActive ? 1 : 0.70;
            PrimaryActiveBadge.Background = BrushOf(secondaryActive ? "#272624" : "#453E3220");
            SecondaryActiveBadge.Background = BrushOf(secondaryActive ? "#453E3220" : "#272624");
            ((TextBlock)PrimaryActiveBadge.Child).Text = secondaryActive ? "INATIVA" : "ATIVA";
            ((TextBlock)SecondaryActiveBadge.Child).Text = secondaryActive ? "ATIVA" : "INATIVA";
        }

        private void ShowFeedback(string message, bool success)
        {
            ShowFeedbackIn(FeedbackText, message, success);
        }

        private void ShowTransferFeedback(string message, bool success) { ShowFeedbackIn(TransferFeedbackText, message, success); }

        private void ShowFeedbackIn(TextBlock target, string message, bool success)
        {
            target.Text = message;
            target.Foreground = BrushOf(success ? "#7EE2A8" : "#FF7B8A");
            target.Opacity = 1;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(2.2) };
            target.BeginAnimation(OpacityProperty, fade);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
        private void Minimize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
        private void PresetsNavigation_Click(object sender, RoutedEventArgs e) { SetSettingsPage(false); }
        private void AdvancedNavigation_Click(object sender, RoutedEventArgs e)
        {
            if(AdvancedPage.Visibility==Visibility.Visible) { AdvancedNavigationButton.IsChecked=true; return; }
            if(!ConfirmDiscardDraft()) { AdvancedNavigationButton.IsChecked=false; return; }
            ClearPresetDraft(); movementRevision++;
            bool initialSecondary=secondaryActive;
            AdvancedPage.Configure(presetService,()=>backupService.CreateBackup(),p=> { bool testSecondary=secondaryActive; RefreshOrganization(p); secondaryActive=testSecondary; UpdateWeaponStatus(); movementRevision++; },selectedPreset);
            AdvancedPage.SelectTestWeapon(initialSecondary);
            MainPage.Visibility=SettingsPage.Visibility=EditorPage.Visibility=Visibility.Collapsed;
            AdvancedPage.Visibility=Visibility.Visible;
            PresetsNavigationButton.IsChecked=SettingsNavigationButton.IsChecked=false; AdvancedNavigationButton.IsChecked=true;
            if(capturingAction!=null)CancelCapture();
        }
        private void SettingsNavigation_Click(object sender, RoutedEventArgs e) { SetSettingsPage(true); }
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SetSettingsPage(SettingsPage.Visibility!=Visibility.Visible);
        }
        private void SetSettingsPage(bool showSettings)
        {
            if(AdvancedPage.Visibility==Visibility.Visible && !AdvancedPage.ConfirmDiscard())
            { PresetsNavigationButton.IsChecked=SettingsNavigationButton.IsChecked=false; AdvancedNavigationButton.IsChecked=true; return; }
            AdvancedPage.Visibility=Visibility.Collapsed;
            AdvancedNavigationButton.IsChecked=false;
            if(EditorPage.Visibility==Visibility.Visible)
            {
                if(!ConfirmDiscardDraft()) return;
                ClearPresetDraft();
            }
            EditorPage.Visibility=Visibility.Collapsed;
            MainPage.Visibility=showSettings?Visibility.Collapsed:Visibility.Visible;
            SettingsPage.Visibility=showSettings?Visibility.Visible:Visibility.Collapsed;
            PresetsNavigationButton.IsChecked=!showSettings;
            SettingsNavigationButton.IsChecked=showSettings;
            if(!showSettings&&capturingAction!=null)CancelCapture();
            if(showSettings)HotkeyItems.Focus();
        }
        private static string GetApplicationVersion(){var a=Assembly.GetEntryAssembly();var v=a==null?null:a.GetCustomAttribute<AssemblyInformationalVersionAttribute>();return v==null?"2.0.0":v.InformationalVersion;}
        private void InitializeUpdateUi(){loadingUpdatePreferences=true;InstalledVersionText.Text="Versão instalada: "+updateService.LocalVersion;AutoUpdateCheck.IsChecked=updatePreferences.CheckAutomatically;OpenAfterUpdateCheck.IsChecked=updatePreferences.ReopenAfterUpdate;AskBeforeDownloadCheck.IsChecked=updatePreferences.AskBeforeDownload;AskBeforeInstallCheck.IsChecked=updatePreferences.AskBeforeInstall;KeepPreviousVersionCheck.IsChecked=updatePreferences.KeepPreviousVersion;loadingUpdatePreferences=false;if(updatePreferences.CheckAutomatically)Dispatcher.BeginInvoke(new Action(async delegate{await CheckForUpdatesAsync(false);}));}
        private void UpdatePreference_Changed(object sender,RoutedEventArgs e){if(loadingUpdatePreferences||updatePreferences==null)return;updatePreferences.CheckAutomatically=AutoUpdateCheck.IsChecked==true;updatePreferences.ReopenAfterUpdate=OpenAfterUpdateCheck.IsChecked==true;updatePreferences.AskBeforeDownload=AskBeforeDownloadCheck.IsChecked==true;updatePreferences.AskBeforeInstall=AskBeforeInstallCheck.IsChecked==true;updatePreferences.KeepPreviousVersion=KeepPreviousVersionCheck.IsChecked==true;try{updatePreferencesStorage.Save(updatePreferences);}catch(Exception ex){UpdateStatusText.Text="Não foi possível salvar as preferências: "+ex.Message;}}
        private async void CheckUpdates_Click(object sender,RoutedEventArgs e){await CheckForUpdatesAsync(true);}
        private async Task CheckForUpdatesAsync(bool reportCurrent)
        {
            if(updateCancellation!=null)return;
            DateTime now=DateTime.UtcNow;
            if(updatePreferences.NextUpdateCheckUtc.HasValue&&updatePreferences.NextUpdateCheckUtc.Value>now)
            {
                StartUpdateCooldown(updatePreferences.NextUpdateCheckUtc.Value,false);
                return;
            }
            StopUpdateCooldown();
            updateCancellation=new CancellationTokenSource();CheckUpdatesButton.IsEnabled=false;CheckUpdateButtons(false);UpdateStatusText.Text="Verificando atualizações...";UpdateService.Log("Check started; local version "+updateService.LocalVersion);
            try
            {
                UpdateInfo info=await updateService.CheckAsync(updateCancellation.Token);
                updatePreferences.NextUpdateCheckUtc=DateTime.UtcNow.Add(SuccessfulUpdateCheckCooldown);SaveUpdatePreferences();
                LastUpdateCheckText.Text="Última verificação: "+DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                if(info==null){UpdateStatusText.Text=reportCurrent?"Você está usando a versão mais recente.":"Verificação concluída. Nenhuma atualização encontrada.";UpdateService.Log("No newer version found");}
                else{UpdateStatusText.Text="Nova versão disponível: "+info.Version+(info.FileSize.HasValue?" · "+FormatBytes(info.FileSize.Value):"");DownloadUpdateButton.IsEnabled=true;UpdateService.Log("Version found: "+info.Version);}
            }
            catch(UpdateRateLimitException ex)
            {
                updatePreferences.NextUpdateCheckUtc=ex.RetryAfterUtc;SaveUpdatePreferences();
                StartUpdateCooldown(ex.RetryAfterUtc,true);
                LastUpdateCheckText.Text="Última tentativa: "+DateTime.Now.ToString("dd/MM/yyyy HH:mm");UpdateService.Log("Update rate limit; retry after "+ex.RetryAfterUtc.ToString("o"));
            }
            catch(OperationCanceledException){UpdateStatusText.Text="Atualização cancelada.";}
            catch(Exception ex)
            {
                updatePreferences.NextUpdateCheckUtc=DateTime.UtcNow.AddSeconds(30);SaveUpdatePreferences();
                UpdateStatusText.Text="Não foi possível verificar atualizações. "+ex.Message+" Aguarde 30 segundos antes de tentar novamente.";
                LastUpdateCheckText.Text="Última tentativa: "+DateTime.Now.ToString("dd/MM/yyyy HH:mm");UpdateService.Log("Check failed: "+ex.Message);
            }
            finally{updateCancellation.Dispose();updateCancellation=null;CheckUpdatesButton.IsEnabled=true;CheckUpdateButtons(true);}
        }
        private void StartUpdateCooldown(DateTime untilUtc,bool rateLimited)
        {
            updatePreferences.NextUpdateCheckUtc=untilUtc;updateCooldownFromRateLimit=rateLimited;
            if(updateCooldownTimer==null){updateCooldownTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};updateCooldownTimer.Tick+=UpdateCooldownTimer_Tick;}
            UpdateCooldownTimer_Tick(null,null);if(updatePreferences.NextUpdateCheckUtc.HasValue&&updatePreferences.NextUpdateCheckUtc.Value>DateTime.UtcNow)updateCooldownTimer.Start();
        }
        private void UpdateCooldownTimer_Tick(object sender,EventArgs e)
        {
            if(!updatePreferences.NextUpdateCheckUtc.HasValue){StopUpdateCooldown();return;}
            TimeSpan remaining=updatePreferences.NextUpdateCheckUtc.Value-DateTime.UtcNow;
            if(remaining<=TimeSpan.Zero)
            {
                StopUpdateCooldown();updatePreferences.NextUpdateCheckUtc=null;SaveUpdatePreferences();
                UpdateStatusText.Text=updateCooldownFromRateLimit?"A verificação de atualizações está disponível novamente.":"Você já pode verificar atualizações novamente.";updateCooldownFromRateLimit=false;return;
            }
            string wait=FormatWait(remaining);
            UpdateStatusText.Text=updateCooldownFromRateLimit
                ?"O serviço de atualizações limitou as verificações. Aguarde até "+updatePreferences.NextUpdateCheckUtc.Value.ToLocalTime().ToString("HH:mm")+" (aprox. "+wait+") para verificar novamente."
                :"Você já verificou há pouco. Não clique várias vezes seguidas. Aguarde "+wait+" para tentar de novo.";
        }
        private void StopUpdateCooldown(){if(updateCooldownTimer!=null)updateCooldownTimer.Stop();}
        private void SaveUpdatePreferences(){try{updatePreferencesStorage.Save(updatePreferences);}catch(Exception ex){UpdateService.Log("Could not persist update cooldown: "+ex.Message);}}
        private static string FormatWait(TimeSpan wait){if(wait<TimeSpan.Zero)wait=TimeSpan.Zero;if(wait.TotalMinutes>=1)return Math.Ceiling(wait.TotalMinutes).ToString("0")+" minuto(s)";return Math.Max(1,(int)Math.Ceiling(wait.TotalSeconds)).ToString()+" segundo(s)";}
        private async void DownloadUpdate_Click(object sender,RoutedEventArgs e)
        {
            if(updateService.Available==null)return;if(AskBeforeDownloadCheck.IsChecked==true&&MessageBox.Show(this,"Baixar a versão "+updateService.Available.Version+"? Seus dados serão preservados.","Baixar atualização",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            StopUpdateCooldown();
            updateCancellation=new CancellationTokenSource();UpdateProgress.Value=0;UpdateProgress.Visibility=Visibility.Visible;CancelUpdateButton.Visibility=Visibility.Visible;DownloadUpdateButton.IsEnabled=false;UpdateStatusText.Text="Baixando atualização...";UpdateService.Log("Download started");var progress=new Progress<double>(v=>UpdateProgress.Value=v);
            try{updateDownloadPath=await updateService.DownloadAsync(progress,updateCancellation.Token);UpdateStatusText.Text="Atualização pronta para instalar.";InstallUpdateButton.IsEnabled=true;UpdateService.Log("Download completed and validated");}catch(OperationCanceledException){UpdateStatusText.Text="Atualização cancelada.";UpdateService.Log("Download cancelled");}catch(Exception ex){UpdateStatusText.Text="Falha ao baixar a atualização. "+ex.Message;UpdateService.Log("Download failed: "+ex.Message);DownloadUpdateButton.IsEnabled=true;}finally{updateCancellation.Dispose();updateCancellation=null;CancelUpdateButton.Visibility=Visibility.Collapsed;UpdateProgress.Visibility=Visibility.Collapsed;}
        }
        private void InstallUpdate_Click(object sender,RoutedEventArgs e)
        {
            if(string.IsNullOrEmpty(updateDownloadPath))return;if(AskBeforeInstallCheck.IsChecked==true&&MessageBox.Show(this,"Uma nova versão está disponível. Seus presets, atalhos e backups serão preservados durante a atualização. Criar backup e instalar agora?","Instalar atualização",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            try{UpdateStatusText.Text="Preparando backup e instalação...";updateService.Install(updatePreferences.ReopenAfterUpdate,Process.GetCurrentProcess().Id,updatePreferences.KeepPreviousVersion,updatePreferences.PreviousVersionsToKeep);UpdateStatusText.Text="O aplicativo será fechado e o instalador será aberto para concluir a atualização.";Close();}catch(Exception ex){UpdateStatusText.Text="Não foi possível instalar: "+ex.Message;UpdateService.Log("Install failed: "+ex.Message);MessageBox.Show(this,ex.Message,"Falha na atualização",MessageBoxButton.OK,MessageBoxImage.Error);}
        }
        private void CancelUpdate_Click(object sender,RoutedEventArgs e){if(updateCancellation!=null)updateCancellation.Cancel();}
        private void CheckUpdateButtons(bool enabled){if(enabled)DownloadUpdateButton.IsEnabled=updateService.Available!=null&&string.IsNullOrEmpty(updateDownloadPath);else DownloadUpdateButton.IsEnabled=false;}
        private static string FormatBytes(long n){return n<1024*1024?(n/1024.0).ToString("0")+" KB":(n/(1024.0*1024)).ToString("0.0")+" MB";}
        private void Window_Closed(object sender, EventArgs e) { capturingAction=null; if(hotkeyMessageTimer!=null)hotkeyMessageTimer.Stop(); if(hotkeyService!=null)hotkeyService.Dispose(); inputService.Dispose(); }

        private static int Clamp(int value, int minimum, int maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }
        private static SolidColorBrush BrushOf(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
    }
}
