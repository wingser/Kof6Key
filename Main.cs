// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;

namespace Demo
{
    public partial class Main : Form
    {
        private IKeyboardMouseEvents m_Events;
        private bool b6key = true;

        public Main()
        {
            InitializeComponent();
            radioGlobal.Checked = true;
            SubscribeGlobal();
            FormClosing += Main_Closing;
        }

        private void Main_Closing(object sender, CancelEventArgs e)
        {
            Unsubscribe();
        }

        private void SubscribeApplication()
        {
            Unsubscribe();
            Subscribe(Hook.AppEvents());
        }

        private void SubscribeGlobal()
        {
            Unsubscribe();
            Subscribe(Hook.GlobalEvents());
        }

        private void Subscribe(IKeyboardMouseEvents events)
        {
            m_Events = events;
            m_Events.KeyDown += OnKeyDown;
            m_Events.KeyUp += OnKeyUp;
            m_Events.KeyPress += HookManager_KeyPress;
        }

        private void Unsubscribe()
        {
            if (m_Events == null) return;
            m_Events.KeyDown -= OnKeyDown;
            m_Events.KeyUp -= OnKeyUp;
            m_Events.KeyPress -= HookManager_KeyPress;

            m_Events.Dispose();
            m_Events = null;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            Log(string.Format("KeyDown  \t\t {0}\n", e.KeyCode));

            // q代表键盘1方向，e代表键盘3方向
            if (b6key)
            {
                if (e.KeyCode == Keys.Q)
                {
                    SendKeys.SendWait("(as)");
                }
                else if (e.KeyCode == Keys.E)
                {
                    SendKeys.SendWait("(ds)");
                }
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            Log(string.Format("KeyUp  \t\t\t {0}\n", e.KeyCode));

            // 处理热键开关，当发现弹起→按键的时候，切换生效状态。
            if (e.KeyCode == Keys.Right)
            {
                if (b6key)
                {
                    Log("关闭6键");
                    radioGlobal.Checked = false;
                    radioNone.Checked = true;
                    b6key = false;
                }
                else
                {
                    Log("开启6键");
                    radioGlobal.Checked = true;
                    radioNone.Checked = false;
                    b6key = true;
                }
            }

            // pause热键改为显示隐藏程序
            if (e.KeyCode == Keys.Pause)
            {
                if (this.Visible == true)
                {
                    this.Visible = false;
                }
                else
                {
                    this.Visible = true;
                }
            }
        }

        private void HookManager_KeyPress(object sender, KeyPressEventArgs e)
        {
            Log(string.Format("KeyPress \t\t\t {0}\n", e.KeyChar));
            // 长按连击，只处理UI按键，并且开启了长按连击，因为模拟按键也有键盘事件，会有死循环。后面想办法处理
            /*
             * 
            if(bAutoFire)
            {
                Thread.Sleep(10);
                if (e.KeyChar == 'u')
                { 
                    SendKeys.SendWait("{u}");
                }else if (e.KeyChar == 'i')
                {
                    SendKeys.SendWait("{i}");
                }
            }
            */
        }

        private void Log(string text)
        {
            if (IsDisposed) return;
            textBoxLog.AppendText(text);
            textBoxLog.AppendText(Environment.NewLine);
            textBoxLog.ScrollToCaret();
        }

        private void radioGlobal_CheckedChanged(object sender, EventArgs e)
        {
            //if (((RadioButton)sender).Checked) SubscribeGlobal();
            if (((RadioButton)sender).Checked) b6key = true;
        }

        private void radioNone_CheckedChanged(object sender, EventArgs e)
        {
            // if (((RadioButton)sender).Checked) Unsubscribe();
            if (((RadioButton)sender).Checked) b6key = false;
        }

        private void clearLog_Click(object sender, EventArgs e)
        {
            textBoxLog.Clear();
        }

        /// <summary>
        /// 加双击托盘图标的处理程序。
        /// 双击的时候，切换显示隐藏。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (this.Visible == true)
            {
                this.Visible = false;
            }
            else
            {
                this.Visible = true;
            }
        }

        private void Main_Load(object sender, EventArgs e)
        {
            SubscribeGlobal();
        }
    }
}