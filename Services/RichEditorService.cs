using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace Mooc.Services
{
    public class RichEditorService
    {
        private readonly IJSRuntime _jsRuntime;
        
        public RichEditorService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }
        
        public async Task InitializeEditorAsync(string elementId, string content)
        {
            await _jsRuntime.InvokeVoidAsync("initRichEditor", elementId, content);
        }
        
        public async Task<string> GetEditorContentAsync(string elementId)
        {
            return await _jsRuntime.InvokeAsync<string>("getRichEditorContent", elementId);
        }
        
        public async Task DestroyEditorAsync(string elementId)
        {
            await _jsRuntime.InvokeVoidAsync("destroyRichEditor", elementId);
        }
        
        public async Task SetEditorReadOnlyAsync(string elementId, bool isReadOnly)
        {
            await _jsRuntime.InvokeVoidAsync("setRichEditorReadOnly", elementId, isReadOnly);
        }
        
        // Nouvelle méthode pour vérifier si l'éditeur existe
        public async Task<bool> EditorExistsAsync(string elementId)
        {
            return await _jsRuntime.InvokeAsync<bool>("editorExists", elementId);
        }
    }
}