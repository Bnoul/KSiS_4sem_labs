using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using P2P_Chat.Core;
using P2P_Chat.Models;

namespace P2P_Chat.GUI_stuff
{
    public class View_model : INotifyPropertyChanged
    {
        private readonly Chat _node;

        public ObservableCollection<Chat_event> Events { get; } = new();
        private string _inputText = "";
        public string InputText
        {
            get => _inputText;
            set { _inputText = value; OnPropertyChanged(); }
        }

        public ICommand SendCommand { get; }

        public View_model(string name, IPAddress ip)
        {
            _node = new Chat(name, ip);
            _node.OnEvent += ev =>
            {
                App.Current.Dispatcher.Invoke(() => Events.Add(ev));
            };
            _node.OnIncomingMessage += (remoteName, remoteIp, text) =>
            {
                // уже добавлено в историю, можно при желании что-то ещё
            };

            foreach (var ev in _node.History)
                Events.Add(ev);

            SendCommand = new RelayCommand(_ => Send(), _ => !string.IsNullOrWhiteSpace(InputText));

            _node.Start();
        }

        private void Send()
        {
            var text = InputText.Trim();
            if (string.IsNullOrEmpty(text)) return;
            _node.SendTextMessage(text);
            InputText = "";
        }

        public void Stop()
        {
            _node.Stop();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
