using System.Windows.Forms;

namespace Demo
{
    partial class Main
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Main));
            radioGlobal = new RadioButton();
            textBoxLog = new TextBox();
            groupBox2 = new GroupBox();
            label1 = new Label();
            clearLogButton = new Button();
            radioNone = new RadioButton();
            notifyIcon1 = new NotifyIcon(components);
            groupBox2.SuspendLayout();
            SuspendLayout();
            // 
            // radioGlobal
            // 
            radioGlobal.AutoSize = true;
            radioGlobal.BackColor = System.Drawing.Color.White;
            radioGlobal.Checked = true;
            radioGlobal.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            radioGlobal.Location = new System.Drawing.Point(10, 34);
            radioGlobal.Margin = new Padding(5, 4, 5, 4);
            radioGlobal.Name = "radioGlobal";
            radioGlobal.Size = new System.Drawing.Size(57, 21);
            radioGlobal.TabIndex = 10;
            radioGlobal.TabStop = true;
            radioGlobal.Text = "启动";
            radioGlobal.UseVisualStyleBackColor = false;
            radioGlobal.CheckedChanged += radioGlobal_CheckedChanged;
            // 
            // textBoxLog
            // 
            textBoxLog.BackColor = System.Drawing.Color.White;
            textBoxLog.BorderStyle = BorderStyle.FixedSingle;
            textBoxLog.Dock = DockStyle.Fill;
            textBoxLog.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            textBoxLog.Location = new System.Drawing.Point(0, 125);
            textBoxLog.Margin = new Padding(5, 4, 5, 4);
            textBoxLog.Multiline = true;
            textBoxLog.Name = "textBoxLog";
            textBoxLog.ReadOnly = true;
            textBoxLog.ScrollBars = ScrollBars.Vertical;
            textBoxLog.Size = new System.Drawing.Size(358, 0);
            textBoxLog.TabIndex = 8;
            textBoxLog.WordWrap = false;
            // 
            // groupBox2
            // 
            groupBox2.BackColor = System.Drawing.Color.White;
            groupBox2.Controls.Add(label1);
            groupBox2.Controls.Add(clearLogButton);
            groupBox2.Controls.Add(radioNone);
            groupBox2.Controls.Add(radioGlobal);
            groupBox2.Dock = DockStyle.Top;
            groupBox2.FlatStyle = FlatStyle.Flat;
            groupBox2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            groupBox2.Location = new System.Drawing.Point(0, 0);
            groupBox2.Margin = new Padding(5, 4, 5, 4);
            groupBox2.Name = "groupBox2";
            groupBox2.Padding = new Padding(5, 4, 5, 4);
            groupBox2.Size = new System.Drawing.Size(358, 125);
            groupBox2.TabIndex = 9;
            groupBox2.TabStop = false;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.BackColor = System.Drawing.Color.SeaShell;
            label1.BorderStyle = BorderStyle.FixedSingle;
            label1.Location = new System.Drawing.Point(153, 12);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(180, 70);
            label1.TabIndex = 17;
            label1.Text = "【→】：启动停止热键\r\n【Pause】: 显示隐藏程序\r\n启动后，增加1、3方向键；\r\nQ→1、E→3。";
            // 
            // clearLogButton
            // 
            clearLogButton.Location = new System.Drawing.Point(26, 87);
            clearLogButton.Margin = new Padding(5, 4, 5, 4);
            clearLogButton.Name = "clearLogButton";
            clearLogButton.Size = new System.Drawing.Size(113, 38);
            clearLogButton.TabIndex = 16;
            clearLogButton.Text = "Clear Log";
            clearLogButton.UseVisualStyleBackColor = true;
            clearLogButton.Click += clearLog_Click;
            // 
            // radioNone
            // 
            radioNone.AutoSize = true;
            radioNone.BackColor = System.Drawing.Color.White;
            radioNone.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            radioNone.Location = new System.Drawing.Point(77, 34);
            radioNone.Margin = new Padding(5, 4, 5, 4);
            radioNone.Name = "radioNone";
            radioNone.Size = new System.Drawing.Size(57, 21);
            radioNone.TabIndex = 14;
            radioNone.Text = "停止";
            radioNone.UseVisualStyleBackColor = false;
            radioNone.CheckedChanged += radioNone_CheckedChanged;
            // 
            // notifyIcon1
            // 
            notifyIcon1.Icon = (System.Drawing.Icon)resources.GetObject("notifyIcon1.Icon");
            notifyIcon1.Text = "6键键盘";
            notifyIcon1.Visible = true;
            notifyIcon1.MouseDoubleClick += notifyIcon1_MouseDoubleClick;
            // 
            // Main
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(358, 84);
            Controls.Add(textBoxLog);
            Controls.Add(groupBox2);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Margin = new Padding(5, 4, 5, 4);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Main";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Kof防冲突键盘设置";
            Load += Main_Load;
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private System.Windows.Forms.RadioButton radioGlobal;
        private System.Windows.Forms.TextBox textBoxLog;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.RadioButton radioNone;
        private System.Windows.Forms.Button clearLogButton;
        private System.Windows.Forms.Label label1;
        private NotifyIcon notifyIcon1;
    }
}

