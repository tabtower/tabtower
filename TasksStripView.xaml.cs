using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SessionDeck.ViewModels;

namespace SessionDeck;

/// <summary>The collapsed tasks strip; actions delegate to MainWindow.</summary>
public partial class TasksStripView : UserControl
{
    private MainWindow? Owner => Window.GetWindow(this) as MainWindow;

    public TasksStripView() => InitializeComponent();

    private void OpenPage_Click(object sender, RoutedEventArgs e) => Owner?.ShowTasksPage();

    private void Square_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskItemViewModel task } el)
        {
            Owner?.HandleTaskActivate(task, el);
            e.Handled = true;
        }
    }
}
