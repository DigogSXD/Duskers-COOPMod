using System;
using DuskersCoopMod.Save;
using UnityEngine;

namespace DuskersCoopMod.UI
{
    public class SaveSlotMenuScreen : MenuScreenClass
    {
        public SaveSlotMenuScreen() : base(null)
        {
        }

        protected override void Initialize()
        {
            ActiveText = "Save Slot Manager";
            IgnoreCancel = false;
        }

        public override void LoadMenu()
        {
            MenuPanelUI.Instance.Clear();
            int num = 0;

            var header = new DuskersMenuItem("=== SELECT ACTIVE SAVE SLOT ===", KeyCode.None, null, num++);
            header.Disabled = true;
            header.OverridenColor = Color.cyan;
            MenuPanelUI.Instance.AddMenuItem(header);

            for (int i = 1; i <= 3; i++)
            {
                int slotNum = i;
                bool isActive = SaveSlotManager.CurrentSlot == slotNum;
                string summary = SaveSlotManager.GetSlotSummary(slotNum);
                string activeTag = isActive ? " [ACTIVE]" : "";
                string label = $"[{slotNum}] Slot {slotNum}: {summary}{activeTag}";

                KeyCode key = KeyCode.None;
                if (slotNum == 1) key = KeyCode.Alpha1;
                else if (slotNum == 2) key = KeyCode.Alpha2;
                else if (slotNum == 3) key = KeyCode.Alpha3;

                var item = new DuskersMenuItem(label, key, (m) =>
                {
                    SaveSlotManager.SwitchSlot(slotNum);
                    RefreshScreen();
                }, num++);

                if (isActive)
                {
                    item.OverridenColor = Color.green;
                }

                MenuPanelUI.Instance.AddMenuItem(item);
            }

            MenuPanelUI.Instance.AddMenuItem(null);
            num++;

            // Option to delete current slot
            MenuPanelUI.Instance.AddMenuItem(new DuskersMenuItem($"[D]elete Slot {SaveSlotManager.CurrentSlot} Data", KeyCode.D, (m) =>
            {
                int slotToDelete = SaveSlotManager.CurrentSlot;
                DialogUI.Instance.ShowDialog(
                    $"Delete Slot {slotToDelete}?",
                    $"Are you sure you want to erase all data in Slot {slotToDelete}?\r\n\r\nThis cannot be undone.",
                    ModalWindowType.YesNo,
                    (res, inp) =>
                    {
                        if (res == ModalWindowResult.Yes)
                        {
                            SaveSlotManager.DeleteSlot(slotToDelete);
                            RefreshScreen();
                        }
                    }
                );
            }, num++));

            base.LoadMenu();
        }

        public void RefreshScreen()
        {
            MenuPanelUI.Instance.Clear();
            MenuPanelUI.Instance.Reset();
            LoadMenu();
        }
    }
}
