using System.Windows.Forms;

namespace Ampersand.Mozart.WEB.Services;

/// <summary>
/// Windows file dialog. Runs on a dedicated STA thread because the Blazor circuit is not STA.
/// </summary>
public sealed class NativeFilePicker
{
    public Task<IReadOnlyList<string>> PickProjectsAsync(string? initialDirectory = null)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<string>>();

        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Select project or solution",
                    Filter = "Projects and solutions|*.sln;*.slnx;*.csproj;*.fsproj;*.vbproj|Solutions|*.sln;*.slnx|C# projects|*.csproj|All files|*.*",
                    Multiselect = true,
                    CheckFileExists = true,
                    RestoreDirectory = true,
                };

                if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    dialog.InitialDirectory = initialDirectory;
                }

                tcs.SetResult(dialog.ShowDialog() == DialogResult.OK
                    ? dialog.FileNames
                    : []);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }
}
