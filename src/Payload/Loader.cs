using System;
using UnityEngine;

namespace HFTrainer
{
    /// <summary>Точка входа, которую вызывает инжектор через mono_runtime_invoke.</summary>
    public static class Loader
    {
        private static GameObject _go;
        private static bool _pending;

        public static void Load()
        {
            try
            {
                Log.Write("Load() вызван, версия payload " + typeof(Loader).Assembly.GetName().Version);
                if (_go != null) { Log.Write("Трейнер уже загружен"); return; }
                try
                {
                    Create();
                }
                catch (Exception e)
                {
                    // Некоторые версии Unity запрещают создавать объекты не из главного потока.
                    // Тогда откладываем создание до ближайшего кадра (колбэк вызывается в главном потоке).
                    Log.Write("Прямое создание не удалось (" + e.GetType().Name + "), откладываю до кадра");
                    if (!_pending)
                    {
                        _pending = true;
                        Camera.onPreRender += OnPreRender;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("Load error: " + e);
                throw;
            }
        }

        private static void OnPreRender(Camera cam)
        {
            Camera.onPreRender -= OnPreRender;
            _pending = false;
            try { if (_go == null) Create(); }
            catch (Exception e) { Log.Write("Отложенное создание: " + e); }
        }

        private static void Create()
        {
            var go = new GameObject("HFTrainer");
            go.AddComponent<TrainerBehaviour>();
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            _go = go;
            Log.Write("Трейнер создан");
        }

        public static void Unload()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            Log.Write("Трейнер выгружен");
        }
    }
}
