using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;

namespace SimpleMyMemo
{
    public partial class Form1 : Form
    {
        // Win32 API 선언 - 클립보드 변경 감지를 위한 코드
        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardViewer(IntPtr hWndNewViewer);

        [DllImport("user32.dll")]
        private static extern bool ChangeClipboardChain(IntPtr hWndRemove, IntPtr hWndNewNext);

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        // 클립보드 관련 상수
        private const int WM_DRAWCLIPBOARD = 0x0308;
        private const int WM_CHANGECBCHAIN = 0x030D;

        private IntPtr nextClipboardViewer;
        private string lastClipboardText = "";
        private bool isMonitoring = false;
        private Thread clipboardThread;
        private string lastImageHash = "";

        private List<Task> tasks = new List<Task>();
        private List<Color> colors = new List<Color>
        {
            Color.FromArgb(255, 235, 238), // red-100
            Color.FromArgb(227, 242, 253), // blue-100
            Color.FromArgb(232, 245, 233), // green-100
            Color.FromArgb(255, 253, 231), // yellow-100
            Color.FromArgb(243, 229, 245), // purple-100
            Color.FromArgb(252, 228, 236), // pink-100
            Color.FromArgb(232, 234, 246), // indigo-100
            Color.FromArgb(224, 242, 241), // teal-100
        };

        public Form1()
        {
            InitializeComponent();
            UpdateProgress();
            
            // 이미지 해시 초기화
            lastImageHash = "";
            
            // 클립보드 모니터링 시작
            StartClipboardMonitoring();
            
            // 폼 종료 이벤트 처리
            this.FormClosing += Form1_FormClosing;
        }
        
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 클립보드 모니터링 중지
            StopClipboardMonitoring();
        }

        private void btnAddTask_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtNewTask.Text))
            {
                AddNewTask(txtNewTask.Text);
                txtNewTask.Text = string.Empty;
            }
        }
        
        private void AddNewTask(string text, Image image = null)
        {
            Color color = colors[tasks.Count % colors.Count];
            Task newTask = new Task
            {
                Id = DateTime.Now.Ticks,
                Text = text,
                Completed = false,
                Color = color,
                CreatedAt = DateTime.Now,
                Type = image != null ? TaskType.Image : TaskType.Text,
                TaskImage = image
            };
            
            tasks.Add(newTask);
            RefreshTaskList();
            UpdateProgress();
        }

        private void RefreshTaskList()
        {
            flowLayoutPanel1.Controls.Clear();
            foreach (var task in tasks)
            {
                Panel panel;
                
                if (task.Type == TaskType.Text)
                {
                    // 텍스트 항목 패널
                    panel = new Panel
                    {
                        Width = flowLayoutPanel1.Width - 25,
                        Height = 40,
                        BackColor = task.Color
                    };

                    CheckBox checkBox = new CheckBox
                    {
                        Text = task.Text,
                        Checked = task.Completed,
                        AutoSize = true,
                        Location = new Point(5, 10)
                    };
                    checkBox.CheckedChanged += (s, e) => 
                    {
                        task.Completed = checkBox.Checked;
                        UpdateProgress();
                    };

                    Button deleteButton = new Button
                    {
                        Text = "Delete",
                        Location = new Point(panel.Width - 80, 5),
                        Size = new Size(70, 30)
                    };
                    deleteButton.Click += (s, e) =>
                    {
                        tasks.Remove(task);
                        RefreshTaskList();
                        UpdateProgress();
                    };

                    panel.Controls.Add(checkBox);
                    panel.Controls.Add(deleteButton);
                }
                else // TaskType.Image
                {
                    // 이미지 항목 패널 (더 크게 만듦)
                    panel = new Panel
                    {
                        Width = flowLayoutPanel1.Width - 25,
                        Height = 200, // 이미지를 위해 더 큰 높이
                        BackColor = task.Color,
                        Padding = new Padding(5)
                    };
                    
                    // 이미지 표시를 위한 라벨
                    Label titleLabel = new Label
                    {
                        Text = task.Text,
                        AutoSize = true,
                        Location = new Point(5, 5),
                        Font = new Font("Segoe UI", 9, FontStyle.Bold)
                    };
                    
                    // 이미지 표시를 위한 PictureBox
                    PictureBox pictureBox = new PictureBox
                    {
                        Width = panel.Width - 20,
                        Height = 150,
                        Location = new Point(10, 25),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Image = task.TaskImage,
                        BorderStyle = BorderStyle.FixedSingle
                    };
                    
                    CheckBox checkBox = new CheckBox
                    {
                        Text = "Completed",
                        Checked = task.Completed,
                        AutoSize = true,
                        Location = new Point(10, 175)
                    };
                    checkBox.CheckedChanged += (s, e) => 
                    {
                        task.Completed = checkBox.Checked;
                        UpdateProgress();
                    };

                    Button deleteButton = new Button
                    {
                        Text = "Delete",
                        Location = new Point(panel.Width - 80, 175),
                        Size = new Size(70, 23)
                    };
                    deleteButton.Click += (s, e) =>
                    {
                        tasks.Remove(task);
                        RefreshTaskList();
                        UpdateProgress();
                    };

                    panel.Controls.Add(titleLabel);
                    panel.Controls.Add(pictureBox);
                    panel.Controls.Add(checkBox);
                    panel.Controls.Add(deleteButton);
                }
                
                flowLayoutPanel1.Controls.Add(panel);
            }
        }

        private void UpdateProgress()
        {
            int completedTasks = tasks.Count(t => t.Completed);
            int totalTasks = tasks.Count;
            progressBar1.Maximum = Math.Max(1, totalTasks);
            progressBar1.Value = completedTasks;
            lblProgress.Text = $"Progress: {completedTasks} / {totalTasks} tasks completed";
        }
        
        // 클립보드 모니터링 시작
        private void StartClipboardMonitoring()
        {
            if (isMonitoring)
                return;
                
            isMonitoring = true;
            nextClipboardViewer = SetClipboardViewer(this.Handle);
            
            // 백그라운드 스레드에서 클립보드 모니터링
            clipboardThread = new Thread(new ThreadStart(ClipboardMonitoringThread))
            {
                IsBackground = true
            };
            clipboardThread.Start();
        }
        
        // 클립보드 모니터링 중지
        private void StopClipboardMonitoring()
        {
            if (!isMonitoring)
                return;
                
            isMonitoring = false;
            ChangeClipboardChain(this.Handle, nextClipboardViewer);
            
            if (clipboardThread != null && clipboardThread.IsAlive)
            {
                clipboardThread.Abort();
            }
        }
        
        // 클립보드 모니터링 스레드
        private void ClipboardMonitoringThread()
        {
            while (isMonitoring)
            {
                try
                {
                    // UI 스레드에서 클립보드 내용 확인
                    this.Invoke(new Action(() =>
                    {
                        ProcessClipboardData();
                    }));
                }
                catch (Exception ex)
                {
                    // 오류 처리
                }
                
                // 1초마다 체크
                Thread.Sleep(1000);
            }
        }
        
        // 클립보드 데이터 처리
        private void ProcessClipboardData()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string clipText = Clipboard.GetText();
                    
                    // 이전에 저장한 텍스트와 다른 경우에만 저장
                    if (!string.IsNullOrEmpty(clipText) && clipText != lastClipboardText)
                    {
                        lastClipboardText = clipText;
                        AddNewTask($"[Clipboard] {clipText}");
                    }
                }
                else if (Clipboard.ContainsImage())
                {
                    try
                    {
                        // 클립보드에서 이미지 가져오기
                        Image clipImage = Clipboard.GetImage();
                        
                        if (clipImage != null)
                        {
                            // 이미지 해시 계산
                            string imageHash = CalculateImageHash(clipImage);
                            
                            // 이전 이미지와 다른 경우에만 추가
                            if (imageHash != lastImageHash)
                            {
                                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                AddNewTask($"[Clipboard] Image copied at {timestamp}", clipImage);
                                lastImageHash = imageHash;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 이미지 처리 오류 발생시 텍스트만 추가
                        AddNewTask($"[Clipboard] Failed to process image: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // 오류 처리
            }
        }
        
        // 이미지 해시 계산 - 두 이미지가 동일한지 비교하기 위한 함수
        private string CalculateImageHash(Image img)
        {
            try
            {
                // 이미지를 작은 크기로 줄여서 해시 계산 (성능 향상)
                using (Bitmap smallBmp = new Bitmap(img, new Size(16, 16)))
                using (MemoryStream ms = new MemoryStream())
                {
                    smallBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    byte[] bytes = ms.ToArray();
                    
                    // 간단한 해시 계산
                    using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                    {
                        byte[] hash = md5.ComputeHash(bytes);
                        return BitConverter.ToString(hash).Replace("-", "").ToLower();
                    }
                }
            }
            catch
            {
                // 해시 계산 오류 발생 시 경우 무작위 문자열 반환
                return Guid.NewGuid().ToString();
            }
        }
        
        // 윈도우 메시지 처리 오버라이드
        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_DRAWCLIPBOARD:
                    // 클립보드 내용이 변경됨
                    ProcessClipboardData();
                    
                    // 다음 클립보드 뷰어에게 메시지 전달
                    SendMessage(nextClipboardViewer, m.Msg, m.WParam, m.LParam);
                    break;
                    
                case WM_CHANGECBCHAIN:
                    // 클립보드 체인이 변경됨
                    if (m.WParam == nextClipboardViewer)
                        nextClipboardViewer = m.LParam;
                    else if (nextClipboardViewer != IntPtr.Zero)
                        SendMessage(nextClipboardViewer, m.Msg, m.WParam, m.LParam);
                    break;
            }
            
            base.WndProc(ref m);
        }
    }

    public enum TaskType
    {
        Text,
        Image
    }
    
    public class Task
    {
        public long Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool Completed { get; set; }
        public Color Color { get; set; }
        public DateTime CreatedAt { get; set; }
        public TaskType Type { get; set; } = TaskType.Text;
        public Image TaskImage { get; set; } = null;
    }
}
