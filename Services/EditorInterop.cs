using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mooc.Services
{
    public static class EditorInterop
    {
        private static Dictionary<string, Action<string, string>> _contentChangedCallbacks = 
            new Dictionary<string, Action<string, string>>();

        // Méthode appelée depuis JavaScript
        [JSInvokable]
        public static Task OnEditorContentChanged(string elementId, string content)
        {
            if (_contentChangedCallbacks.ContainsKey(elementId))
            {
                _contentChangedCallbacks[elementId].Invoke(elementId, content);
            }
            
            return Task.CompletedTask;
        }

        // S'abonner aux changements de contenu pour un éditeur spécifique
        public static void SubscribeToContentChanges(string elementId, Action<string, string> callback)
        {
            _contentChangedCallbacks[elementId] = callback;
        }

        // Se désabonner des changements
        public static void UnsubscribeFromContentChanges(string elementId)
        {
            if (_contentChangedCallbacks.ContainsKey(elementId))
            {
                _contentChangedCallbacks.Remove(elementId);
            }
        }
    }
}