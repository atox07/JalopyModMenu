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
        private readonly FieldInfo cursorLock;

        public readonly MethodInfo WalkingTick;
        public Behaviour Menu { get { return menuGlobal.GetValue(null) as Behaviour; } }

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
            cursorLock = RequireField(menuType, "lockCursor");
        }

        public bool LocksCursor(Behaviour menu)
        {
            return menu && (bool)cursorLock.GetValue(menu);
        }

        public void SetCursorLock(Behaviour menu, bool enabled)
        {
            if (menu)
                cursorLock.SetValue(menu, enabled);
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
            var menu = Menu;
            if (!menu || !(bool)movementUnlocked.GetValue(menu))
                return false;

            int state = (int)pauseState.GetValue(menu);
            return state == 0 || state == 2;
        }

        public Vector2 ReadMovement(bool reserveMenuArrows)
        {
            var menu = Menu;
            var keys = menu ? assignedKeys.GetValue(menu) as string[] : null;
            return new Vector2(
                ReadDirection(keys, 6, KeyCode.D, reserveMenuArrows) - ReadDirection(keys, 4, KeyCode.A, reserveMenuArrows),
                ReadDirection(keys, 0, KeyCode.W, reserveMenuArrows) - ReadDirection(keys, 2, KeyCode.S, reserveMenuArrows));
        }

        public Vector2 ReadDriving(bool reserveMenuArrows)
        {
            var menu = Menu;
            var keys = menu ? assignedKeys.GetValue(menu) as string[] : null;
            return new Vector2(
                ReadDirection(keys, 14, KeyCode.D, reserveMenuArrows) - ReadDirection(keys, 12, KeyCode.A, reserveMenuArrows),
                ReadDirection(keys, 8, KeyCode.W, reserveMenuArrows) - ReadDirection(keys, 10, KeyCode.S, reserveMenuArrows));
        }

        public bool WorldItemActionPressed(bool drop)
        {
            var menu = Menu;
            var keys = menu ? assignedKeys.GetValue(menu) as string[] : null;
            int index = drop ? 18 : 16;
            if (keys == null || keys.Length <= index + 1)
                return Input.GetButtonDown(drop ? "Drop" : "Pickup");

            return (!string.IsNullOrEmpty(keys[index]) && Input.GetKeyDown(keys[index])) ||
                (!string.IsNullOrEmpty(keys[index + 1]) && Input.GetKeyDown(keys[index + 1]));
        }

        private static int ReadDirection(string[] keys, int index, KeyCode fallback, bool reserveMenuArrows)
        {
            string primary = ReadMovementKey(keys, index, reserveMenuArrows);
            string secondary = ReadMovementKey(keys, index + 1, reserveMenuArrows);
            if (string.IsNullOrEmpty(primary) && string.IsNullOrEmpty(secondary))
                return Input.GetKey(fallback) ? 1 : 0;

            return IsPressed(primary) || IsPressed(secondary) ? 1 : 0;
        }

        private static string ReadMovementKey(string[] keys, int index, bool reserveMenuArrows)
        {
            string key = keys != null && index < keys.Length ? keys[index] : null;
            if (reserveMenuArrows && (key == "up" || key == "down" || key == "left" || key == "right"))
                return null;

            return key;
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
