using System.Reflection;

var assembly = Assembly.Load("VFL.GeradorWebMToken");
var formType = assembly.GetType("VFL.GeradorWebMToken.MainForm", true)!;
using var form = (Form)Activator.CreateInstance(formType, nonPublic: true)!;
form.Show();
Application.DoEvents();
form.PerformLayout();
Application.DoEvents();
using var image = new Bitmap(form.Width, form.Height);
form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
image.Save(args[0]);
form.Close();
