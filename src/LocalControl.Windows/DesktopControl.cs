using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using LocalControl.Core;
using Microsoft.Win32;

namespace LocalControl.Windows;

internal static class DesktopControl
{
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput { public ushort Key,Scan; public uint Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, Input[] inputs,int size);
    [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool GetUserObjectInformation(IntPtr obj,int index,StringBuilder text,int length,out int needed);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool LockWorkStation();
    [DllImport("powrprof.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)]bool hibernate,[MarshalAs(UnmanagedType.U1)]bool force,[MarshalAs(UnmanagedType.U1)]bool disableWake);
    [DllImport("powrprof.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool IsPwrSuspendAllowed();
    [DllImport("powrprof.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool IsPwrHibernateAllowed();
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr guid);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static PowerCapabilities Capabilities()=>new(IsPwrSuspendAllowed(),IsPwrHibernateAllowed());
    public static void Power(string action)
    {
        switch(action) {
            case "lock": if(!LockWorkStation())throw new Win32Exception();break;
            case "sleep": if(!IsPwrSuspendAllowed()||!SetSuspendState(false,false,false))throw new ControlException("sleep_unavailable","Windows не разрешает сон.",409);break;
            case "hibernate": if(!IsPwrHibernateAllowed()||!SetSuspendState(true,false,false))throw new ControlException("hibernate_unavailable","Гибернация не включена в Windows.",409);break;
            case "restart": case "shutdown":
                var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"shutdown.exe")){UseShellExecute=false,CreateNoWindow=true};
                info.ArgumentList.Add(action=="restart"?"/r":"/s");info.ArgumentList.Add("/t");info.ArgumentList.Add("0");
                using(var process=Process.Start(info)){if(process is null||!process.WaitForExit(3000)||process.ExitCode!=0)throw new ControlException("power_failed","Windows отклонила запрос питания.",409);}break;
            default:throw new ControlException("invalid_power","Неизвестное действие питания.");
        }
    }
    private static void RequireInteractiveDesktop()
    {
        var desktop=OpenInputDesktop(0,false,1);
        if(desktop==IntPtr.Zero)throw new ControlException("desktop_locked","Экран заблокирован или открыт защищённый диалог Windows.",409);
        try { var name=new StringBuilder(256);if(!GetUserObjectInformation(desktop,2,name,512,out _)||name.ToString()!="Default")throw new ControlException("desktop_locked","Защищённый рабочий стол недоступен.",409); }
        finally { CloseDesktop(desktop); }
    }
    public static MonitorInfo[] Monitors()=>Screen.AllScreens.Select((s,i)=>new MonitorInfo(s.DeviceName,$"Экран {i+1}",s.Bounds.Width,s.Bounds.Height,s.Primary)).ToArray();
    private static Screen Monitor(string id)=>Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==id)??throw new ControlException("monitor_gone","Монитор отключён.",404);
    public static byte[] Capture(string id)
    {
        RequireInteractiveDesktop();var screen=Monitor(id);var bounds=screen.Bounds;
        if((long)bounds.Width*bounds.Height>40000000)throw new ControlException("monitor_large","Разрешение монитора слишком велико.",409);
        using var raw=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format24bppRgb);
        using(var graphics=Graphics.FromImage(raw))graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,bounds.Size,CopyPixelOperation.SourceCopy);
        var scale=Math.Min(1,1600.0/bounds.Width);using var result=new Bitmap(raw,new Size((int)(bounds.Width*scale),(int)(bounds.Height*scale)));
        using var stream=new MemoryStream();var codec=ImageCodecInfo.GetImageEncoders().First(x=>x.FormatID==ImageFormat.Jpeg.Guid);
        using var options=new EncoderParameters(1);options.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,75L);result.Save(stream,codec,options);return stream.ToArray();
    }
    private static void Send(Input[] inputs)
    {
        if(inputs.Length==0)return;
        if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Input>())==inputs.Length)return;
        var releases=inputs.Where(x=>x.Type==1&&(x.Data.Key.Flags&2)!=0||x.Type==0&&(x.Data.Mouse.Flags&20)!=0).ToArray();
        if(releases.Length>0)SendInput((uint)releases.Length,releases,Marshal.SizeOf<Input>());
        throw new ControlException("input_blocked","Windows отклонила ввод. Elevated окна и UAC недоступны.",409);
    }
    private static Input Key(ushort key,bool up=false,bool unicode=false)=>new(){Type=1,Data=new(){Key=new(){Key=unicode?(ushort)0:key,Scan=unicode?key:(ushort)0,Flags=(up?2u:0u)|(unicode?4u:0u)}}};
    public static void InputAction(string monitor,RemoteInput value)
    {
        RequireInteractiveDesktop();var screen=Monitor(monitor);
        if(!double.IsFinite(value.X)||!double.IsFinite(value.Y)||value.X is <0 or >1||value.Y is <0 or >1)throw new ControlException("invalid_coordinates","Координаты вне экрана.");
        switch(value.Kind){
            case "click": case "rightClick":
                var bounds=SystemInformation.VirtualScreen;var x=screen.Bounds.Left+Math.Round(value.X*(screen.Bounds.Width-1));var y=screen.Bounds.Top+Math.Round(value.Y*(screen.Bounds.Height-1));
                var move=new Input{Type=0,Data=new(){Mouse=new(){X=(int)Math.Round((x-bounds.Left)*65535/Math.Max(1,bounds.Width-1)),Y=(int)Math.Round((y-bounds.Top)*65535/Math.Max(1,bounds.Height-1)),Flags=0xC001}}};
                Send([move,new(){Type=0,Data=new(){Mouse=new(){Flags=value.Kind=="click"?2u:8u}}},new(){Type=0,Data=new(){Mouse=new(){Flags=value.Kind=="click"?4u:16u}}}]);break;
            case "scroll":if(Math.Abs((long)value.Delta)>1200)throw new ControlException("invalid_scroll","Слишком большой scroll.");Send([new(){Type=0,Data=new(){Mouse=new(){Data=unchecked((uint)value.Delta),Flags=0x800}}}]);break;
            case "text":
                if(value.Text is null||value.Text.Length>512||value.Text.Any(c=>char.IsControl(c)&&c!='\n'&&c!='\t'))throw new ControlException("invalid_text","Текст ограничен 512 символами.");
                Send(value.Text.SelectMany(c=>new[]{Key(c,unicode:true),Key(c,true,true)}).ToArray());break;
            case "key":
                var keys=new Dictionary<string,ushort>{{"Enter",13},{"Escape",27},{"Tab",9},{"Backspace",8},{"Delete",46},{"ArrowLeft",37},{"ArrowUp",38},{"ArrowRight",39},{"ArrowDown",40},{"Space",32}};
                if(value.Key is null||!keys.TryGetValue(value.Key,out var code))throw new ControlException("invalid_key","Клавиша не разрешена.");Send([Key(code),Key(code,true)]);break;
            default:throw new ControlException("invalid_input","Неизвестный тип ввода.");
        }
        // Every action is a complete down/up sequence; no held key survives a
        // disconnect/revoke, and no remote arbitrary virtual-key API exists.
    }
    public static void Hotkey(int[] keys)
    {
        RequireInteractiveDesktop();if(keys.Length is <2 or >4||keys.Any(x=>x is <8 or >165)||!keys.Any(x=>x is 16 or 17 or 18))throw new ControlException("invalid_hotkey","Нужна комбинация из 2–4 клавиш с Ctrl/Alt/Shift.");
        try { Send(keys.Select(x=>Key((ushort)x)).ToArray()); }
        finally { Send(keys.Reverse().Select(x=>Key((ushort)x,true)).ToArray()); }
    }
    public static T ClipboardSta<T>(Func<T> work)
    {
        T result=default!;Exception? error=null;var thread=new Thread(()=>{try{result=work();}catch(Exception ex){error=ex;}}){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(error is not null)throw new ControlException("clipboard_busy","Буфер обмена занят другой программой. Повторите действие.",409);return result;
    }
    private static string[] Query(string query,string property)
    {
        using var search=new ManagementObjectSearcher(query);search.Options.Timeout=TimeSpan.FromSeconds(3);using var results=search.Get();var list=new List<string>();
        foreach(ManagementObject item in results)using(item)if(item[property]?.ToString() is string value)list.Add(value);return list.ToArray();
    }
    public static HardwareInfo Hardware()
    {
        var warnings=new List<string>();string Read(string query,string property){try{return Query(query,property).FirstOrDefault()??"Недоступно";}catch(ManagementException){warnings.Add("Часть WMI данных недоступна.");return "Недоступно";}}
        string[] gpus;try{gpus=Query("SELECT Name FROM Win32_VideoController","Name");}catch(ManagementException){gpus=[];warnings.Add("GPU metadata недоступны.");}
        string plan="Недоступно";if(PowerGetActiveScheme(IntPtr.Zero,out var pointer)==0){try{plan=Marshal.PtrToStructure<Guid>(pointer).ToString();}finally{LocalFree(pointer);}}
        var networks=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up).Select(x=>{var stats=x.GetIPv4Statistics();return new NetworkInfo(x.Name,x.NetworkInterfaceType.ToString(),x.GetIPProperties().UnicastAddresses.Select(a=>a.Address.ToString()).ToArray(),stats.BytesReceived,stats.BytesSent);}).ToArray();
        return new(Read("SELECT Caption FROM Win32_OperatingSystem","Caption"),Environment.UserName,Read("SELECT Name FROM Win32_Processor","Name"),gpus,Read("SELECT Product FROM Win32_BaseBoard","Product"),plan,networks,Monitors(),warnings.Distinct().ToArray());
    }
}
