using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace PrintFreeTool;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private KeyboardHook? _keyboardHook;
    private TrayController? _tray;
    private CaptureOverlay? _captureOverlay;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private EditorWindow? _editorWindow;
    private bool _isInstantCaptureRunning;
    private PrintScreenSettings _printScreenSettings = PrintScreenSettings.Default;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool isElevatedReplacement = e.Args.Contains("--replace-elevated", StringComparer.OrdinalIgnoreCase);
        _singleInstanceMutex = new Mutex(true, @"Local\PrintFreeTool.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            if (!isElevatedReplacement || !WaitForPreviousInstance())
            {
                System.Windows.MessageBox.Show(
                    "O PrintFreeTool já está em execução na bandeja do sistema.",
                    "PrintFreeTool",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        HotkeyShortcut shortcut = SettingsService.LoadShortcut();
        if (shortcut == HotkeyShortcut.EditorDefault || shortcut == HotkeyShortcut.ActiveWindowDefault)
        {
            shortcut = HotkeyShortcut.Default;
            SettingsService.SaveShortcut(shortcut);
        }
        _printScreenSettings = SettingsService.LoadPrintScreenSettings();

        bool isAdministrator = ElevationService.IsAdministrator;
        _tray = new TrayController(shortcut, isAdministrator);
        _tray.CaptureRequested += ShowDirectCaptureOverlay;
        _tray.EditorCaptureRequested += ShowEditorCaptureOverlay;
        _tray.ActiveWindowCaptureRequested += CaptureActiveWindowImmediately;
        _tray.GameModeRequested += RestartAsAdministrator;
        _tray.OpenRequested += ShowMainWindow;
        _tray.ExitRequested += ExitApplication;

        _mainWindow = new MainWindow(shortcut);
        _mainWindow.CaptureRequested += ShowDirectCaptureOverlay;
        _mainWindow.OpenFolderRequested += CaptureService.OpenCaptureFolder;
        _mainWindow.SettingsRequested += ShowSettings;
        _keyboardHook = new KeyboardHook(shortcut);
        _keyboardHook.HotkeyPressed += ShowDirectCaptureOverlay;
        _keyboardHook.EditorHotkeyPressed += ShowEditorCaptureOverlay;
        _keyboardHook.ActiveWindowHotkeyPressed += CaptureActiveWindowImmediately;
        _keyboardHook.PrintScreenPressed += CaptureFullScreenImmediately;

        try
        {
            _keyboardHook.Start();
            _tray.ShowMessage(
                isAdministrator ? "Modo jogos ativo" : "PrintFreeTool está pronto",
                isAdministrator
                    ? "Capturas em jogos elevados estão habilitadas."
                    : $"Pressione {shortcut.DisplayText} para selecionar uma área da tela.");
        }
        catch (Exception exception)
        {
            _tray.ShowMessage("Não foi possível ativar o atalho", exception.Message, isError: true);
            System.Windows.MessageBox.Show(
                $"Não foi possível ativar {shortcut.DisplayText}.\n\n{exception.Message}",
                "PrintFreeTool",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool WaitForPreviousInstance()
    {
        if (_singleInstanceMutex is null)
        {
            return false;
        }

        try
        {
            return _singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(8));
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private void RestartAsAdministrator()
    {
        if (ElevationService.IsAdministrator)
        {
            _tray?.ShowMessage("Modo jogos ativo", "O PrintFreeTool já está executando como administrador.");
            return;
        }

        try
        {
            string executable = Environment.ProcessPath
                                ?? throw new InvalidOperationException("Não foi possível localizar o executável do PrintFreeTool.");
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--replace-elevated",
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            ExitApplication();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            _tray?.ShowMessage("Modo jogos cancelado", "A permissão de administrador não foi concedida.", isError: true);
        }
        catch (Exception exception)
        {
            _tray?.ShowMessage("Não foi possível ativar o modo jogos", exception.Message, isError: true);
        }
    }

    private void ShowDirectCaptureOverlay() => ShowCaptureOverlay(openEditor: false);

    private void ShowEditorCaptureOverlay() => ShowCaptureOverlay(openEditor: true);

    private void ShowCaptureOverlay(bool openEditor)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (openEditor && _editorWindow is not null)
            {
                _editorWindow.Show();
                _editorWindow.Activate();
                _tray?.ShowMessage("Editor aberto", "Conclua ou feche a captura atual antes de iniciar outra.");
                return;
            }

            if (_captureOverlay is not null)
            {
                _captureOverlay.Activate();
                return;
            }

            _mainWindow?.Hide();
            _captureOverlay = new CaptureOverlay(openEditor);
            _captureOverlay.CaptureReady += OnCaptureReady;
            _captureOverlay.CaptureSaved += OnDirectCaptureSaved;
            _captureOverlay.CaptureCanceled += OnCaptureCanceled;
            _captureOverlay.Show();
        });
    }

    private void OnDirectCaptureSaved(string savedPath)
    {
        _captureOverlay = null;
        _tray?.ShowMessage("Captura concluída", $"Copiada e salva em:\n{savedPath}");
    }

    private void ShowMainWindow()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private void ShowSettings()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_settingsWindow is not null)
            {
                _settingsWindow.Activate();
                return;
            }

            if (_mainWindow is null || _keyboardHook is null)
            {
                return;
            }

            _keyboardHook.Enabled = false;
            _settingsWindow = new SettingsWindow(_keyboardHook.Shortcut, _printScreenSettings)
            {
                Owner = _mainWindow
            };

            try
            {
                bool? saved = _settingsWindow.ShowDialog();
                if (saved == true)
                {
                    HotkeyShortcut shortcut = _settingsWindow.SelectedShortcut;
                    SettingsService.SaveShortcut(shortcut);
                    SettingsService.SavePrintScreenSettings(_settingsWindow.SelectedPrintScreenSettings);
                    _printScreenSettings = _settingsWindow.SelectedPrintScreenSettings;
                    _keyboardHook.Shortcut = shortcut;
                    _mainWindow.UpdateShortcut(shortcut);
                    _tray?.UpdateShortcut(shortcut);
                    _tray?.ShowMessage("Configurações atualizadas", $"Atalho de seleção: {shortcut.DisplayText}");
                }
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show(
                    $"Não foi possível salvar o novo atalho.\n\n{exception.Message}",
                    "PrintFreeTool",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _settingsWindow = null;
                _keyboardHook.Enabled = true;
            }
        });
    }

    private void OnCaptureReady(System.Windows.Media.Imaging.BitmapSource image)
    {
        _captureOverlay = null;
        _editorWindow = new EditorWindow(image);
        _editorWindow.EditingCompleted += OnEditingCompleted;
        _editorWindow.Closed += (_, _) => _editorWindow = null;
        _editorWindow.Show();
    }

    private void OnEditingCompleted(string savedPath)
    {
        _tray?.ShowMessage(
            "Captura concluída",
            $"Copiada e salva em:\n{savedPath}");
    }

    private void OnCaptureCanceled()
    {
        _captureOverlay = null;
    }

    private void CaptureFullScreenImmediately()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (_isInstantCaptureRunning || _captureOverlay is not null)
            {
                return;
            }

            _isInstantCaptureRunning = true;
            try
            {
                if (_printScreenSettings.Mode == PrintScreenCaptureMode.EachScreenSeparately)
                {
                    IReadOnlyList<ScreenCapture> captures = CaptureService.CaptureEachScreen();
                    IReadOnlyList<string> paths = await CaptureService.SaveSeparateScreensAndCopyAsync(captures);
                    _tray?.ShowMessage(
                        "Telas capturadas separadamente",
                        $"{paths.Count} arquivos salvos. A tela principal foi copiada.");
                }
                else
                {
                    System.Windows.Media.Imaging.BitmapSource image = CaptureService.CaptureConfiguredScreens(_printScreenSettings);
                    string path = await CaptureService.SaveAndCopyAsync(image);
                    _tray?.ShowMessage("Tela capturada", $"Copiada e salva em:\n{path}");
                }
            }
            catch (Exception exception)
            {
                _tray?.ShowMessage("Falha na captura", exception.Message, isError: true);
            }
            finally
            {
                _isInstantCaptureRunning = false;
            }
        });
    }

    private void CaptureActiveWindowImmediately()
    {
        IntPtr activeWindow = CaptureService.GetActiveWindowHandle();

        Dispatcher.BeginInvoke(async () =>
        {
            if (_isInstantCaptureRunning || _captureOverlay is not null)
            {
                return;
            }

            _isInstantCaptureRunning = true;
            try
            {
                ActiveWindowSnapshot capture = await CaptureService.CaptureActiveWindowHighQualityAsync(activeWindow);
                string path = await CaptureService.SaveAndCopyAsync(capture.Image, capture.Title);
                string displayTitle = capture.Title.Length > 70 ? capture.Title[..70] + "…" : capture.Title;
                _tray?.ShowMessage("Janela ativa capturada", $"{displayTitle}\nCopiada e salva em:\n{path}");
            }
            catch (ProtectedContentException exception)
            {
                _tray?.ShowMessage(
                    "Captura protegida",
                    $"{exception.Message} Nenhum arquivo foi salvo.",
                    isError: true);
            }
            catch (Exception exception)
            {
                _tray?.ShowMessage("Falha ao capturar a janela", exception.Message, isError: true);
            }
            finally
            {
                _isInstantCaptureRunning = false;
            }
        });
    }

    private void ExitApplication()
    {
        _captureOverlay?.Close();
        _settingsWindow?.Close();
        _editorWindow?.Close();
        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _keyboardHook?.Dispose();
        _tray?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was not owned by this process.
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
