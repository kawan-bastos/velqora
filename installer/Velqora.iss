#ifndef AppVersion
  #error AppVersion must be supplied by build-installer.ps1
#endif
#ifndef Payload
  #error Payload must be supplied by build-installer.ps1
#endif
#ifndef Output
  #error Output must be supplied by build-installer.ps1
#endif
#ifdef SmokeTest
  #define ProductName "Velqora Installer Test"
  #define ProductId "{12F2AD76-5E8D-409F-B24B-974879DD2EF4}"
  #define RunningMutex "Velqora_Installer_Smoke_3E9F"
#else
  #define ProductName "Velqora"
  #define ProductId "{67CBF56E-7743-4B2B-AD62-80EC4CFB32D5}"
  #define RunningMutex "MacroDoButler_UnicaInstancia_6B5E"
#endif

[Setup]
AppId={{#ProductId}
AppName={#ProductName}
AppVersion={#AppVersion}
AppVerName=Velqora {#AppVersion}
VersionInfoVersion={#AppVersion}.0
AppPublisher=Velqora
DefaultDirName={localappdata}\Programs\Velqora
DefaultGroupName=Velqora
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
SetupIconFile=..\assets\Velqora.ico
UninstallDisplayName={#ProductName}
UninstallDisplayIcon={app}\Velqora.exe
OutputDir={#Output}
OutputBaseFilename=Velqora-Setup-Windows-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dark includetitlebar
AppMutex={#RunningMutex}
CloseApplications=no
RestartApplications=no
MinVersion=10.0
LicenseFile=..\LICENSE
UninstallLogMode=append

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; Flags: unchecked

[Files]
Source: "{#Payload}\Velqora.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\Velqora.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
; Only recreate the legacy filename if it existed here before this installation.
Source: "{#Payload}\Velqora.exe"; DestDir: "{app}"; DestName: "Macro do Butler.exe"; Flags: ignoreversion; Check: LegacySpaced
Source: "{#Payload}\Velqora.exe.config"; DestDir: "{app}"; DestName: "Macro do Butler.exe.config"; Flags: ignoreversion; Check: LegacySpaced
Source: "{#Payload}\Velqora.exe"; DestDir: "{app}"; DestName: "Macro.do.Butler.exe"; Flags: ignoreversion; Check: LegacyDotted
Source: "{#Payload}\Velqora.exe.config"; DestDir: "{app}"; DestName: "Macro.do.Butler.exe.config"; Flags: ignoreversion; Check: LegacyDotted

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\Velqora.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#ProductName}"; Filename: "{app}\Velqora.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Velqora.exe"; Description: "Abrir o Velqora (macro inicialmente desligado)"; Flags: nowait postinstall skipifsilent unchecked shellexec

[Code]
function GetFileAttributes(Path: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

var
  PriorUsePage, ImportPage: TInputOptionWizardPage;
  PresetFilePage: TInputFileWizardPage;
  HadSpaced, HadDotted: Boolean;
  RemoveData: Boolean;
  DowngradeConsentKey: String;

function LegacySpaced: Boolean;
begin Result := HadSpaced; end;
function LegacyDotted: Boolean;
begin Result := HadDotted; end;

function InstalledUninstaller(var Uninstaller: String): Boolean;
var Key, Command, AppPath: String;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ProductId}_is1';
  Result := RegKeyExists(HKCU, Key);
  Uninstaller := '';
  if Result then begin
    if RegQueryStringValue(HKCU, Key, 'UninstallString', Command) then
      Uninstaller := RemoveQuotes(Trim(Command));
    if (Uninstaller = '') and RegQueryStringValue(HKCU, Key, 'Inno Setup: App Path', AppPath) then
      Uninstaller := AddBackslash(AppPath) + 'unins000.exe';
  end;
  if not Result then begin
    AppPath := ExpandConstant('{localappdata}\Programs\{#ProductName}');
    if FileExists(AddBackslash(AppPath) + 'unins000.exe') then begin
      Result := True;
      Uninstaller := AddBackslash(AppPath) + 'unins000.exe';
    end;
  end;
end;

function IsApplicationUpdate: Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/UPDATE') = 0 then begin
      Result := True;
      exit;
    end;
end;

function ConfirmDowngrade(ExecutablePath: String; UseRegisteredVersion: Boolean): Boolean;
var OldVersion, ConsentKey: String; OldPacked, NewPacked: Int64;
begin
  Result := True;
  if not GetVersionNumbersString(ExecutablePath, OldVersion) then begin
    if not UseRegisteredVersion then exit;
    if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ProductId}_is1', 'DisplayVersion', OldVersion) then exit;
  end;
  if not StrToVersion(OldVersion, OldPacked) or not StrToVersion('{#AppVersion}.0', NewPacked) then exit;
  if ComparePackedVersion(OldPacked, NewPacked) <= 0 then exit;
  ConsentKey := LowerCase(ExecutablePath) + '|' + OldVersion;
  if DowngradeConsentKey = ConsentKey then exit;
  Result := False;
  if WizardSilent then begin
    Log('Downgrade blocked: installed ' + OldVersion + ', requested {#AppVersion}. Presets from newer versions may be incompatible.');
    exit;
  end;
  Result := SuppressibleMsgBox('Você está tentando voltar do Velqora ' + OldVersion + ' para a versão {#AppVersion}.' + #13#10#13#10 +
    'Presets salvos por versões mais novas podem não abrir na versão anterior. Por exemplo, a 3.2 não reconhece os presets avançados da 4.0.' + #13#10#13#10 +
    'Seus arquivos serão preservados, mas não serão convertidos para o formato antigo. Antes de continuar, exporte uma cópia pelo aplicativo atual.' + #13#10#13#10 +
    'Para continuar usando os presets atuais, escolha Não e mantenha a versão mais nova.' + #13#10#13#10 +
    'Deseja continuar com a versão anterior?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
  if Result then DowngradeConsentKey := ConsentKey;
end;

function InitializeSetup: Boolean;
var Release: Cardinal; Uninstaller: String; ExitCode: Integer;
#ifdef SmokeTest
  I: Integer; HasSmokeUninstallFlag: Boolean;
#endif
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
  if Result then Result := Release >= 528040;
  if not Result then begin
    SuppressibleMsgBox('O Velqora requer .NET Framework 4.8 ou superior. Instale o pacote oficial da Microsoft e execute este instalador novamente. Nenhum download será iniciado automaticamente.', mbError, MB_OK, IDOK);
    exit;
  end;
  if not InstalledUninstaller(Uninstaller) then exit;
  if not ConfirmDowngrade(AddBackslash(ExtractFileDir(Uninstaller)) + 'Velqora.exe', True) then begin
    Result := False;
    exit;
  end;
  if IsApplicationUpdate then exit;
  Result := False;
  if WizardSilent then begin
#ifdef SmokeTest
    HasSmokeUninstallFlag := False;
    for I := 1 to ParamCount do
      if CompareText(ParamStr(I), '/SMOKEUNINSTALL') = 0 then HasSmokeUninstallFlag := True;
    if not HasSmokeUninstallFlag then exit;
#else
    exit;
#endif
  end else if SuppressibleMsgBox('O aplicativo já está instalado. Deseja desinstalar? Seus presets e configurações serão preservados.',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) <> IDYES then exit;
  if CheckForMutexes('{#RunningMutex}') then begin
    SuppressibleMsgBox('Feche o Velqora ou Macro do Butler (F12) antes de desinstalar.', mbError, MB_OK, IDOK);
    exit;
  end;
  if not FileExists(Uninstaller) then begin
    SuppressibleMsgBox('A instalação anterior foi encontrada, mas o desinstalador não está disponível. Remova a instalação anterior em Aplicativos instalados do Windows.', mbError, MB_OK, IDOK);
    exit;
  end;
  if not Exec(Uninstaller, '/SILENT /NORESTART', ExtractFileDir(Uninstaller), SW_SHOWNORMAL,
    ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
    SuppressibleMsgBox('A desinstalação não foi concluída. A instalação atual foi mantida.', mbError, MB_OK, IDOK)
  else
    SuppressibleMsgBox('Velqora desinstalado. Seus presets e configurações foram preservados. Para instalar novamente, abra este Setup outra vez.', mbInformation, MB_OK, IDOK);
end;

procedure AddSecondaryNote(Page: TWizardPage; Top: Integer; Caption: String);
var Note: TNewStaticText;
begin
  Note := TNewStaticText.Create(Page);
  Note.Parent := Page.Surface;
  Note.AutoSize := False;
  Note.WordWrap := True;
  Note.SetBounds(ScaleX(8), Top, Page.SurfaceWidth - ScaleX(16), ScaleY(55));
  Note.Caption := Caption;
  Note.StyleElements := Note.StyleElements - [seFont];
  if IsDarkInstallMode then Note.Font.Color := StrToColor('#B8B8B8')
  else Note.Font.Color := StrToColor('#555555');
  Note.AdjustHeight;
end;

procedure InitializeWizard;
begin
  PriorUsePage := CreateInputOptionPage(wpSelectDir,
    'Você já utilizou o Velqora anteriormente?',
    'Escolha uma opção para continuar.', '', True, False);
  PriorUsePage.Add('Sim');
  PriorUsePage.Add('Não');
  PriorUsePage.SelectedValueIndex := 1;
  PriorUsePage.SubCaptionLabel.Visible := False;
  PriorUsePage.CheckListBox.Top := ScaleY(16);
  PriorUsePage.CheckListBox.Height := ScaleY(64);
  ImportPage := CreateInputOptionPage(PriorUsePage.ID,
    'Deseja importar seus presets agora?',
    'Escolha uma opção para continuar.', '', True, False);
  ImportPage.Add('Sim, quero importar');
  ImportPage.Add('Não, continuar a instalação');
  ImportPage.SelectedValueIndex := 1;
  ImportPage.SubCaptionLabel.Visible := False;
  ImportPage.CheckListBox.Top := ScaleY(16);
  ImportPage.CheckListBox.Height := ScaleY(64);
  AddSecondaryNote(ImportPage, ScaleY(116),
    'Se escolher Não, você poderá importar seus presets dentro do aplicativo depois.');
  PresetFilePage := CreateInputFilePage(ImportPage.ID,
    'Selecione o arquivo de presets',
    'Escolha um arquivo salvo anteriormente.', '');
  PresetFilePage.Add('Arquivo de presets:', 'Arquivos de presets (*.json;*.cfg)|*.json;*.cfg', 'json');
  PresetFilePage.SubCaptionLabel.Visible := False;
  PresetFilePage.PromptLabels[0].Top := ScaleY(16);
  PresetFilePage.Edits[0].Top := ScaleY(42);
  PresetFilePage.Buttons[0].Top := PresetFilePage.Edits[0].Top;
  AddSecondaryNote(PresetFilePage, ScaleY(112),
    'Formatos aceitos: presets.json e presets.cfg. O arquivo original será preservado.');
#ifdef SmokePresetFile
  PriorUsePage.SelectedValueIndex := 0;
  ImportPage.SelectedValueIndex := 0;
  PresetFilePage.Values[0] := '{#SmokePresetFile}';
#endif
  WizardForm.WelcomeLabel2.Caption := 'A instalação é feita somente para sua conta do Windows.' + #13#10#13#10 +
    'Seus dados ficam em %LOCALAPPDATA%\Velqora. Ao abrir o aplicativo, o macro começa desligado.';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := ((PageID = ImportPage.ID) and not PriorUsePage.Values[0]) or
    ((PageID = PresetFilePage.ID) and (not PriorUsePage.Values[0] or not ImportPage.Values[0]));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then
    Result := ConfirmDowngrade(ExpandConstant('{app}\Velqora.exe'), False);
  if CurPageID = PresetFilePage.ID then begin
    Result := FileExists(PresetFilePage.Values[0]) and
      ((LowerCase(ExtractFileExt(PresetFilePage.Values[0])) = '.json') or
       (LowerCase(ExtractFileExt(PresetFilePage.Values[0])) = '.cfg'));
    if not Result then SuppressibleMsgBox('Selecione um arquivo de presets .json ou .cfg válido.', mbError, MB_OK, IDOK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Backup, Name: String; I: Integer;
begin
  Result := '';
  if not ConfirmDowngrade(ExpandConstant('{app}\Velqora.exe'), False) then begin
    Result := 'A instalação da versão anterior foi cancelada. Presets de versões mais novas podem ser incompatíveis. Mantenha a versão mais nova para continuar usando seus presets.';
    exit;
  end;
  HadSpaced := FileExists(ExpandConstant('{app}\Macro do Butler.exe'));
  HadDotted := FileExists(ExpandConstant('{app}\Macro.do.Butler.exe'));
  if CheckForMutexes('{#RunningMutex}') then begin
    Result := 'Feche o Velqora ou Macro do Butler (F12) antes de continuar. Nenhum processo será encerrado à força.';
    exit;
  end;
  Backup := ExpandConstant('{app}\previous-versions\') + GetDateTimeString('yyyymmdd-hhnnss', '-', ':');
  for I := 0 to 5 do begin
    case I of
      0: Name := 'Velqora.exe';
      1: Name := 'Velqora.exe.config';
      2: Name := 'Macro do Butler.exe';
      3: Name := 'Macro do Butler.exe.config';
      4: Name := 'Macro.do.Butler.exe';
      5: Name := 'Macro.do.Butler.exe.config';
    end;
    if FileExists(ExpandConstant('{app}\') + Name) then begin
      if not ForceDirectories(Backup) then begin Result := 'Sem permissão para criar a cópia anterior. Escolha uma pasta gravável.'; exit; end;
      if not CopyFile(ExpandConstant('{app}\') + Name, Backup + '\' + Name, True) then begin Result := 'Não foi possível preservar o executável anterior. Feche o aplicativo e verifique as permissões.'; exit; end;
      if GetSHA256OfFile(ExpandConstant('{app}\') + Name) <> GetSHA256OfFile(Backup + '\' + Name) then begin Result := 'Falha ao validar a cópia anterior. A atualização foi cancelada.'; exit; end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Source, Target, Directory, SourceHash: String;
begin
  if (CurStep <> ssPostInstall) or not PriorUsePage.Values[0] or not ImportPage.Values[0] then exit;
  Source := PresetFilePage.Values[0];
  try
  SourceHash := GetSHA256OfFile(Source);
#ifdef SmokeTest
  Directory := ExpandConstant('{localappdata}\Velqora Installer Test\pending-imports');
#else
  Directory := ExpandConstant('{localappdata}\Velqora\pending-imports');
#endif
  Target := Directory + '\' + SourceHash + LowerCase(ExtractFileExt(Source));
  if not ForceDirectories(Directory) or
    (not FileExists(Target) and not CopyFile(Source, Target, True)) or
    (SourceHash <> GetSHA256OfFile(Target)) then
    SuppressibleMsgBox('O Velqora foi instalado, mas não foi possível preparar a importação. Abra o aplicativo e use Importar presets para escolher o arquivo novamente.', mbError, MB_OK, IDOK);
  except
    SuppressibleMsgBox('O Velqora foi instalado, mas o arquivo escolhido não pôde ser copiado. Abra o aplicativo e use Importar presets para escolhê-lo novamente.', mbError, MB_OK, IDOK);
  end;
end;

// Refuse a junction anywhere in the deletion tree or any of its parent paths.
function SafeTree(Path: String): Boolean;
var Find: TFindRec; Parent: String;
begin
  Result := False;
  if (GetFileAttributes(Path) and $400) <> 0 then exit;
  Parent := ExtractFileDir(Path);
  while Length(Parent) > 3 do begin
    if (GetFileAttributes(Parent) and $400) <> 0 then exit;
    Parent := ExtractFileDir(Parent);
  end;
  Result := True;
  if FindFirst(Path + '\*', Find) then try
    repeat
      if (Find.Name <> '.') and (Find.Name <> '..') then begin
        if (Find.Attributes and $400) <> 0 then begin Result := False; exit; end;
        if (Find.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          if not SafeTree(Path + '\' + Find.Name) then begin Result := False; exit; end;
      end;
    until not FindNext(Find);
  finally FindClose(Find); end;
end;

function PersonalDataDirectory: String;
begin
#ifdef SmokeTest
  Result := ExpandConstant('{localappdata}\Velqora Installer Test');
#else
  Result := ExpandConstant('{localappdata}\Velqora');
#endif
end;

function InitializeUninstall: Boolean;
var Form: TSetupForm; Check: TNewCheckBox; Button, CancelButton: TNewButton;
begin
  Result := True; RemoveData := False;
  if UninstallSilent then begin
#ifdef SmokeTest
    // Only the isolated test package can exercise opt-in deletion unattended.
    RemoveData := FileExists(PersonalDataDirectory + '\.smoke-delete-opt-in');
#endif
    exit;
  end;
  Form := CreateCustomForm(ScaleX(540), ScaleY(175), False, False);
  try
    Form.Caption := 'Dados pessoais do Velqora';
    Check := TNewCheckBox.Create(Form); Check.Parent := Form;
    Check.SetBounds(ScaleX(16), ScaleY(16), ScaleX(510), ScaleY(90));
    Check.Caption := 'Apagar também presets, atalhos personalizados, configurações e backups em:' + #13#10 + PersonalDataDirectory;
    Check.Checked := False;
    Button := TNewButton.Create(Form); Button.Parent := Form; Button.Caption := 'Continuar'; Button.ModalResult := mrOK;
    Button.SetBounds(ScaleX(320), ScaleY(125), ScaleX(90), ScaleY(26));
    CancelButton := TNewButton.Create(Form); CancelButton.Parent := Form; CancelButton.Caption := 'Cancelar'; CancelButton.ModalResult := mrCancel;
    CancelButton.SetBounds(ScaleX(425), ScaleY(125), ScaleX(90), ScaleY(26));
    Result := Form.ShowModal = mrOK;
    if Result and Check.Checked then
      RemoveData := SuppressibleMsgBox('Excluir definitivamente somente ' + PersonalDataDirectory + '? As cópias na pasta antiga serão mantidas.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
  finally Form.Free; end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Data: String;
begin
  if (CurUninstallStep = usPostUninstall) and RemoveData then begin
    Data := PersonalDataDirectory;
    if DirExists(Data) then begin
      if not SafeTree(Data) then SuppressibleMsgBox('Dados preservados: foi encontrado um link ou junção. Remova os dados manualmente após verificar o caminho.', mbError, MB_OK, IDOK)
      else if not DelTree(Data, True, True, True) then SuppressibleMsgBox('Alguns dados não puderam ser removidos. Verifique: ' + Data, mbError, MB_OK, IDOK);
    end;
  end;
end;
