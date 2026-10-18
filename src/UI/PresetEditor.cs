using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacroDoButler
{
    public partial class MainWindow
    {
        private PresetEditDraft editDraft;
        private Preset editOriginal;

        private void OpenPresetEditor(Preset preset, string newGame = null)
        {
            if(AdvancedPage.Visibility==Visibility.Visible && !AdvancedPage.ConfirmDiscard())return;
            AdvancedPage.Visibility=Visibility.Collapsed; AdvancedNavigationButton.IsChecked=false;
            bool sameDraft = editDraft != null && (preset == null
                ? editOriginal == null && string.Equals(editDraft.GameName, newGame, StringComparison.OrdinalIgnoreCase)
                : editOriginal != null && PresetService.IdentityEquals(editOriginal, preset));
            if (!sameDraft)
            {
                if (!ConfirmDiscardDraft()) return;
                ClearPresetDraft();
                editOriginal = preset == null ? null : PresetEditingService.Copy(preset);
                var initial = preset ?? new Preset
                {
                    GameName = newGame, Name = string.Empty, CharacterName = "Preset", WeaponName = "Arma não definida",
                    PrimaryVertical = 0, PrimaryHorizontal = 0,
                    SecondaryVertical = 0, SecondaryHorizontal = 0,
                    IntervalMilliseconds = 2, SecondaryIntervalMilliseconds = 2,
                    RequireRightButton = true
                };
                editDraft = new PresetEditDraft(ForceUnits.RoundedCopy(initial), preset == null);
                editDraft.PropertyChanged += EditDraft_PropertyChanged;
                EditorPage.DataContext = editDraft;
                EditorFeedback.Text = string.Empty;
                RefreshLiveEditMovementSettings();
            }
            EditorPageTitle.Text = editOriginal == null ? "Novo Preset" : "Editar preset";
            EditorPageSubtitle.Text = editOriginal == null ? "Prévia ao vivo: alterações afetam o macro temporariamente até salvar ou descartar" : "Prévia ao vivo: alterações afetam o macro temporariamente até salvar ou descartar";
            MainPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            EditorPage.Visibility = Visibility.Visible;
            PresetsNavigationButton.IsChecked = true;
            SettingsNavigationButton.IsChecked = false;
            if (capturingAction != null) CancelCapture();
            EditorName.Focus();
        }

        private bool ConfirmDiscardDraft()
        {
            if (editDraft == null || (!editDraft.IsDirty && !HasEditorErrors(EditorPage))) return true;
            return MessageBox.Show(this, "Descartar as alterações não salvas deste preset?", "Descartar edição",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }
        private void EditDraft_PropertyChanged(object sender, PropertyChangedEventArgs e) { RefreshLiveEditMovementSettings(); }
        private void RefreshLiveEditMovementSettings()
        {
            movementRevision++;
            MovementSettings settings = editDraft == null ? null : editDraft.ToMovementSettings(enabled, secondaryActive);
            if(settings!=null)settings.Revision=movementRevision;
            System.Threading.Volatile.Write(ref liveEditMovementSettings, settings);
        }
        private void EditorBack_Click(object sender, RoutedEventArgs e) { SetSettingsPage(false); }
        private void EditorCancel_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardDraft()) return;
            ClearPresetDraft();
            SetSettingsPage(false);
        }
        private void ClearPresetDraft()
        {
            if (editDraft != null) editDraft.PropertyChanged -= EditDraft_PropertyChanged;
            movementRevision++;
            editDraft = null;
            editOriginal = null;
            System.Threading.Volatile.Write(ref liveEditMovementSettings, null);
            EditorPage.DataContext = null;
            EditorFeedback.Text = string.Empty;
        }
        private void EditorValidationError(object sender, ValidationErrorEventArgs e)
        {
            if(EditorFeedback!=null) EditorFeedback.Text=e.Action==ValidationErrorEventAction.Added?System.Convert.ToString(e.Error.ErrorContent):FirstEditorError(EditorPage) ?? string.Empty;
        }
        private void EditorSave_Click(object sender, RoutedEventArgs e)
        {
            if (editDraft == null) return;
            // Moving focus commits the current textbox before validation.
            ((Button)sender).Focus();
            if (HasEditorErrors(EditorPage)) { EditorFeedback.Text = FirstEditorError(EditorPage) ?? "Confira o valor destacado."; return; }
            try
            {
                var saved = editDraft.ToPreset();
                PresetEditingService.Save(presetService, editOriginal, saved, () => backupService.CreateBackup());
                ClearPresetDraft();
                RefreshOrganization(saved);
                SetSettingsPage(false);
                ShowFeedback("Preset salvo com sucesso.", true);
            }
            catch (Exception exception) { EditorFeedback.Text = "Não foi possível salvar: " + exception.Message; }
        }
        private static bool HasEditorErrors(DependencyObject node)
        {
            if (Validation.GetHasError(node)) return true;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (HasEditorErrors(VisualTreeHelper.GetChild(node, i))) return true;
            return false;
        }
        private static string FirstEditorError(DependencyObject node)
        {
            if(Validation.GetHasError(node)) return System.Convert.ToString(Validation.GetErrors(node)[0].ErrorContent);
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
            {
                var error=FirstEditorError(VisualTreeHelper.GetChild(node,i));
                if(error!=null)return error;
            }
            return null;
        }
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!ConfirmDiscardDraft() || (AdvancedPage.Visibility==Visibility.Visible && !AdvancedPage.ConfirmDiscard())) e.Cancel = true;
        }
    }
}
