using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GCam.UI.ViewModels
{
    /// <summary>Minimal INotifyPropertyChanged plumbing.</summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>Assigns and raises only if the value actually changed.</summary>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            Raise(propertyName);
            return true;
        }
    }
}
