using System;
using System.Drawing;
using System.Windows.Forms;

namespace P15
{
    /// <summary>散热器设置：官方的 通电自动启动 / 智能启停 / 档位数显，以及 开机自动连接</summary>
    static class CoolerSettingsDialog
    {
        public sealed class Result { public bool AutoStart, GearLed, AutoConnect; public int StartStop; }

        public static Result Show(IWin32Window owner, Result init)
        {
            var p = Theme.P;
            using (var f = new GlassForm())
            {
                f.FormBorderStyle = FormBorderStyle.None; f.StartPosition = FormStartPosition.CenterParent;
                f.Size = new Size(340, 404); f.ShowInTaskbar = false; f.KeyPreview = true;
                                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel; };
                var r = new Result { AutoStart = init.AutoStart, GearLed = init.GearLed, AutoConnect = init.AutoConnect, StartStop = init.StartStop };
                int iw = 304;

                var title = new Label2("功能设置") { Font = Theme.H2, Bounds = new Rectangle(18, 14, 300, 26) };
                title.MouseDown += (s, e) => f.DragMove();
                f.Controls.Add(title);

                var tAuto = new Toggle { On = r.AutoStart };
                f.Controls.Add(new SettingRow("通电自启功能", "散热器一通电就开始转，不用按按键", tAuto, 48) { Bounds = new Rectangle(18, 48, iw, 48) });

                f.Controls.Add(new Label2("智能启停") { Bounds = new Rectangle(18, 104, 200, 22) });
                f.Controls.Add(new Label2("跟随电脑开机 / 睡眠 / 关机自动启停", true) { Bounds = new Rectangle(18, 126, 300, 20) });
                string[] ss = { "关闭", "即时运行", "延时运行" };
                var cs = new Choice[3];
                for (int i = 0; i < 3; i++)
                {
                    int idx = i;
                    cs[i] = new Choice(null, ss[i], null) { Bounds = new Rectangle(18 + i * 102, 152, 96, 32) };
                    cs[i].Click += (s, e) => { r.StartStop = idx; for (int k = 0; k < 3; k++) cs[k].Selected = k == idx; };
                    cs[i].Selected = i == r.StartStop;
                    f.Controls.Add(cs[i]);
                }

                var tLed = new Toggle { On = r.GearLed };
                f.Controls.Add(new SettingRow("档位灯显示", "散热器上显示当前档位", tLed, 48) { Bounds = new Rectangle(18, 196, iw, 48) });

                var tConn = new Toggle { On = r.AutoConnect };
                f.Controls.Add(new SettingRow("开机自动连接", "开机后控制中心在后台自动连接散热器并按温度调速", tConn, 56) { Bounds = new Rectangle(18, 250, iw, 56) });

                var ok = new Btn("保存", true) { Bounds = new Rectangle(176, 352, 146, 34) };
                var cancel = new Btn("取消") { Bounds = new Rectangle(18, 352, 146, 34) };
                ok.Click += (s, e) => { r.AutoStart = tAuto.On; r.GearLed = tLed.On; r.AutoConnect = tConn.On; f.DialogResult = DialogResult.OK; };
                cancel.Click += (s, e) => f.DialogResult = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel);
                return f.ShowDialog(owner) == DialogResult.OK ? r : null;
            }
        }
    }
}
