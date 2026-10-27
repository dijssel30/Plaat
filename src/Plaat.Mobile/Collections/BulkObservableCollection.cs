using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Plaat.Mobile.Collections;

/// <summary>
/// An ObservableCollection that can be repopulated with a single Reset notification instead of one
/// notification per item. Plain ObservableCollection.Clear()+Add()-in-a-loop makes CollectionView
/// process one insert/remove per call, which is fine for small edits but becomes an ANR (or eventual
/// OOM under repeated reloads) once a list runs into the tens/hundreds of thousands of items, as
/// with Plaat's full track library.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
