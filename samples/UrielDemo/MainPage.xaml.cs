using Uriel;

namespace UrielDemo;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	private void SetStatus(string action)
		=> StatusLabel.Text = $"{action} done; running={UrielProfiler.IsRunning}";

	private void OnAllocate(object? sender, EventArgs e)
	{
		var buffers = new byte[2000][];
		for (int i = 0; i < buffers.Length; i++)
			buffers[i] = new byte[1024];
		Array.Clear(buffers, 0, buffers.Length);
		buffers = null;
		GC.Collect();
		UrielProfiler.Mark("demo-allocate", 2000);
		SetStatus("allocate");
	}

	private void OnThrow(object? sender, EventArgs e)
	{
		try
		{
			throw new InvalidOperationException("demo fault");
		}
		catch
		{
		}
		SetStatus("throw");
	}

	private void OnHang(object? sender, EventArgs e)
	{
		Thread.Sleep(600);
		SetStatus("hang");
	}

	private void OnCaptureStack(object? sender, EventArgs e)
	{
		UrielProfiler.CaptureStack("demo-stack");
		SetStatus("stack");
	}

	private void OnMark(object? sender, EventArgs e)
	{
		UrielProfiler.Mark("demo-mark", 42, "manual");
		SetStatus("mark");
	}
}
