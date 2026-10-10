using System.Windows;
using System.Windows.Controls;
using TabTower.Services.Phone;

namespace TabTower;

/// <summary>
/// ⚙ → Phone access settings: on/off, the loopback port, the one `tailscale serve` command to
/// run, requests waiting for an answer (deny only: approving needs the code typed into the
/// request's own prompt), and the approved devices with Revoke. Deny and revoke act at once
/// (they are answers, not settings); on/off and the port apply on OK.
/// </summary>
public partial class PhoneAccessDialog : Window
{
    private readonly PhonePairing _pairing;
    private readonly Func<bool> _isRunning;

    public bool EnabledValue => EnabledBox.IsChecked == true;
    public int PortValue { get; private set; }

    public sealed record Row(string StableId, string Title, string Detail);

    public PhoneAccessDialog(bool enabled, int port, PhonePairing pairing, IDeviceResolver tailscale, Func<bool> isRunning)
    {
        InitializeComponent();
        _pairing = pairing;
        _isRunning = isRunning;
        PortValue = port;
        EnabledBox.IsChecked = enabled;
        PortBox.Text = port.ToString();
        StateText.Text = isRunning() ? "running" : enabled ? "not running (see the status bar)" : "";
        RefreshLists();
        UrlText.Text = "Then open this PC's Tailscale address on the phone (looking it up...)";
        _ = ShowUrl(tailscale);
    }

    private async Task ShowUrl(IDeviceResolver tailscale)
    {
        var self = await tailscale.SelfAsync();
        UrlText.Text = self is { DnsName.Length: > 0 }
            ? $"Then open https://{self.DnsName}/ on the phone, with Tailscale connected there as the same user."
            : "Then open this PC's Tailscale address (https://<pc-name>.<tailnet>.ts.net/) on the phone. Tailscale does not seem to be running here right now.";
    }

    private void RefreshLists()
    {
        var pending = _pairing.Pending
            .Select(p => new Row(p.Device.StableId,
                (p.Device.Os.Length > 0 ? $"{p.Device.Name} ({p.Device.Os})" : p.Device.Name) +
                $", node {p.Device.StableId}, asked at {p.At:HH:mm}", "")).ToList();
        PendingList.ItemsSource = pending;
        PendingHeading.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingList.Visibility = PendingHeading.Visibility;
        PendingNote.Visibility = PendingHeading.Visibility;

        var devices = _pairing.Approved
            .OrderByDescending(d => d.LastSeenAt ?? d.ApprovedAt)
            .Select(d => new Row(d.StableId, d.Os.Length > 0 ? $"{d.Name} ({d.Os})" : d.Name,
                $"approved {d.ApprovedAt:dd-MM-yyyy HH:mm}" +
                (d.LastSeenAt is { } seen ? $" · last used {seen:dd-MM-yyyy HH:mm}" : ""))).ToList();
        DevicesList.ItemsSource = devices;
        NoDevicesText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Port_Changed(object sender, TextChangedEventArgs e)
    {
        bool valid = int.TryParse(PortBox.Text.Trim(), out int p) && p is >= 1024 and <= 65535;
        if (valid) PortValue = p;
        if (OkButton != null) OkButton.IsEnabled = valid;
        if (CommandBox != null)
            CommandBox.Text = $"tailscale serve --bg http://127.0.0.1:{(valid ? p : PortValue)}";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(CommandBox.Text); } catch { /* clipboard busy: the text is selectable anyway */ }
    }

    private static string IdOf(object sender) => (sender as FrameworkElement)?.Tag as string ?? "";

    private void DenyPending_Click(object sender, RoutedEventArgs e)
    {
        _pairing.Deny(IdOf(sender));
        RefreshLists();
    }

    private void Revoke_Click(object sender, RoutedEventArgs e)
    {
        _pairing.Revoke(IdOf(sender));
        RefreshLists();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
