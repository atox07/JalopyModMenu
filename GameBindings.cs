using System;
using System.Reflection;
using UnityEngine;

namespace JalopyModMenu
{
    internal sealed class GameBindings
    {
        private readonly Type walkerType;
        private readonly FieldInfo driving;
        private readonly FieldInfo sitting;
        private readonly FieldInfo menuGlobal;
        private readonly FieldInfo pauseState;
        private readonly FieldInfo movementUnlocked;
        private readonly FieldInfo assignedKeys;

        public readonly MethodInfo WalkingTick;

        public GameBindings()
        {
            var game = Assembly.Load("Assembly-CSharp");
            walkerType = game.GetType("RigidbodyControllerC", true);
            var menuType = game.GetType("MainMenuC", true);
            WalkingTick = walkerType.GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            if (WalkingTick == null)
                throw new MissingMethodException(walkerType.FullName, "FixedUpdate");

            driving = RequireField(walkerType, "driving");
            sitting = RequireField(walkerType, "isSat");
            menuGlobal = RequireField(menuType, "Global");
            pauseState = RequireField(menuType, "isPaused");
            movementUnlocked = RequireField(menuType, "hasWokenUp");
            assignedKeys = RequireField(menuType, "assignedInputStrings");
        }

        public Behaviour FindWalker(Camera view)
        {
            return view ? view.GetComponentInParent(walkerType) as Behaviour : null;
        }

        public bool IsSeated(Behaviour walker)
        {
            return (bool)driving.GetValue(walker) || (bool)sitting.GetValue(walker);
        }

        public bool IsWalkingAvailable()
        {
            var menu = menuGlobal.GetValue(null) as Behaviour;
            if (!menu || !(bool)movementUnlocked.GetValue(menu))
                return false;

            int state = (int)pauseState.GetValue(menu);
            return state == 0 || state == 2;
        }

        public Vector2 ReadMovement()
        {
            var menu = menuGlobal.GetValue(null) as Behaviour;
            var keys = menu ? assignedKeys.GetValue(menu) as string[] : null;
            return new Vector2(
                ReadDirection(keys, 6, KeyCode.D) - ReadDirection(keys, 4, KeyCode.A),
                ReadDirection(keys, 0, KeyCode.W) - ReadDirection(keys, 2, KeyCode.S));
        }

        private static int ReadDirection(string[] keys, int index, KeyCode fallback)
        {
            if (keys == null || keys.Length <= index + 1)
                return Input.GetKey(fallback) ? 1 : 0;

            return IsPressed(keys[index]) || IsPressed(keys[index + 1]) ? 1 : 0;
        }

        private static bool IsPressed(string key)
        {
            return !string.IsNullOrEmpty(key) && Input.GetKey(key);
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
