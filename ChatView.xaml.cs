using System.Windows;
using System.Windows.Controls;

namespace YMM4ChatPlugin
{
    public partial class ChatView : UserControl
    {
        public ChatView()
        {
            InitializeComponent();
            this.Loaded += (s, e) =>
            {
                var window = Window.GetWindow(this);
                if (window != null)
                {
                    window.MinWidth = 400;
                    window.MinHeight = 350;
                }
            };
        }
    }
}