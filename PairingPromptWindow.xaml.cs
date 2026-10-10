using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TabTower.Services.Phone;

namespace TabTower;

/// <summary>
/// "A device is asking to use the phone page": the device's tailnet node name, the name it
/// reports for itself, its OS, owner and node id. Approval needs the code shown on the device,
/// typed in here; a wrong code is refused and the request stays open to try again or deny.
/// Closing the window any way other than a successful Approve denies the request (unless it was
/// already answered or expired). The pairing store does the approving, so the code check cannot
/// be skipped by whoever calls this window.
/// </summary>
public partial class PairingPromptWindow : Window
{
    public enum Result { None, Approved, Denied, Expired }

    public Result Outcome { get; private set; } = Result.None;

    private readonly PendingPairing _pending;
    private readonly PhonePairing _pairing;

    public PairingPromptWindow(PendingPairing pending, PhonePairing pairing)
    {
        InitializeComponent();
        _pending = pending;
        _pairing = pairing;
        var d = pending.Device;
        DeviceText.Text = d.Name.Length > 0 ? d.Name : d.StableId;
        var detail = new List<string>();
        if (d.HostName.Length > 0 && !string.Equals(d.HostName, d.Name, StringComparison.OrdinalIgnoreCase))
            detail.Add($"calls itself \"{d.HostName}\"");
        if (d.Os.Length > 0) detail.Add(d.Os);
        if (d.Login.Length > 0) detail.Add(d.Login);
        detail.Add($"node {d.StableId}");
        detail.Add($"asked at {pending.At:HH:mm}");
        DetailText.Text = string.Join(" · ", detail);

        // The request expires on its own; a prompt for an expired request would approve nothing.
        var expire = new DispatcherTimer { Interval = PhonePairing.PendingTtl - (DateTime.Now - pending.At) };
        if (expire.Interval < TimeSpan.FromSeconds(1)) expire.Interval = TimeSpan.FromSeconds(1);
        expire.Tick += (_, _) => { expire.Stop(); Outcome = Result.Expired; Close(); };
        expire.Start();
        Closed += (_, _) => expire.Stop();
        Closing += (_, _) =>
        {
            // X, Esc, Alt+F4 and Deny all land here without an approval: deny, if it is still open.
            if (Outcome is Result.Approved or Result.Expired) return;
            if (_pairing.IsPending(_pending.Device.StableId)) _pairing.Deny(_pending.Device.StableId);
            Outcome = Result.Denied;
        };
    }

    private void Code_Changed(object sender, TextChangedEventArgs e)
    {
        ApproveButton.IsEnabled = CodeBox.Text.Trim().Length == 4;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (_pairing.Approve(_pending.Device.StableId, CodeBox.Text))
        {
            Outcome = Result.Approved;
            Close();
            return;
        }
        if (!_pairing.IsPending(_pending.Device.StableId))
        {
            // Expired or answered elsewhere meanwhile: nothing left to approve.
            Outcome = Result.Expired;
            Close();
            return;
        }
        ErrorText.Text = "That is not the code this device is showing. Check its screen, or deny.";
        ErrorText.Visibility = Visibility.Visible;
        CodeBox.SelectAll();
        CodeBox.Focus();
    }

    private void Deny_Click(object sender, RoutedEventArgs e) => Close();
}
