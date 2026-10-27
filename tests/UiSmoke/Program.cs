using System.Reflection;

var assembly = Assembly.Load("VFL.GeradorWebMToken");
var formType = assembly.GetType("VFL.GeradorWebMToken.MainForm", true)!;
using var form = (Form)Activator.CreateInstance(formType, nonPublic: true)!;
form.Show();
Application.DoEvents();
if (args.Length > 1 && int.TryParse(args[1], out var tabIndex))
{
    var tabs = form.Controls.Cast<Control>().FirstOrDefault(control => control.GetType().Name == "ModeTabControl");
    tabs?.GetType().GetProperty("SelectedIndex")?.SetValue(tabs, tabIndex);
    Application.DoEvents();
}
if (args.Length > 2)
{
    var paths = args.Skip(2).Select(Path.GetFullPath).ToArray();
    var addVideos = formType.GetMethod("AddVideosAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var addTask = (Task)addVideos.Invoke(form, [paths])!;
    while (!addTask.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); }
    addTask.GetAwaiter().GetResult();
    Application.DoEvents();
}
form.PerformLayout();
Application.DoEvents();
foreach (var button in Descendants(form).OfType<Button>().Where(button => button.Visible))
    Console.WriteLine($"BUTTON={button.Text} BOUNDS={button.Bounds}");
using var image = new Bitmap(form.Width, form.Height);
form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
image.Save(args[0]);
form.Close();

static IEnumerable<Control> Descendants(Control root)
{
    foreach (Control child in root.Controls)
    {
        yield return child;
        foreach (var nested in Descendants(child)) yield return nested;
    }
}
