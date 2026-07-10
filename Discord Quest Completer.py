import os
import subprocess
import tkinter as tk
from tkinter import messagebox
import customtkinter as ctk
from pathlib import Path
import threading
import json
import re

# Set Modern Appearance
ctk.set_appearance_mode("Dark")
ctk.set_default_color_theme("blue")

# Windows constant to prevent subprocess from flashing a black CMD window
CREATE_NO_WINDOW = 0x08000000

class EditGameDialog(ctk.CTkToplevel):
    """A custom dialog for editing an existing generated game."""
    def __init__(self, parent, current_name, current_path):
        super().__init__(parent)
        self.title("Edit Game")
        self.geometry("450x250")
        self.resizable(False, False)
        
        self.transient(parent)
        self.grab_set()

        self.result_name = None
        self.result_path = None

        ctk.CTkLabel(self, text="Edit Game Name:", font=ctk.CTkFont(weight="bold")).pack(pady=(15, 0), padx=20, anchor="w")
        self.name_entry = ctk.CTkEntry(self, width=400)
        self.name_entry.insert(0, current_name)
        self.name_entry.pack(pady=(5, 10), padx=20)

        ctk.CTkLabel(self, text="Edit EXE Path:", font=ctk.CTkFont(weight="bold")).pack(pady=0, padx=20, anchor="w")
        self.path_entry = ctk.CTkEntry(self, width=400)
        self.path_entry.insert(0, current_path)
        self.path_entry.pack(pady=(5, 15), padx=20)

        btn_frame = ctk.CTkFrame(self, fg_color="transparent")
        btn_frame.pack(fill="x", padx=20)

        cancel_btn = ctk.CTkButton(btn_frame, text="Cancel", fg_color="gray", command=self.destroy, width=100)
        cancel_btn.pack(side="left")

        save_btn = ctk.CTkButton(btn_frame, text="Save Changes", command=self.save_data, width=150)
        save_btn.pack(side="right")

    def save_data(self):
        self.result_name = self.name_entry.get().strip()
        self.result_path = self.path_entry.get().strip()
        self.destroy()


class DummyExeApp(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("Discord Quest Completer")
        self.geometry("1100x700")
        self.minsize(950, 650)

        self.base_dir = Path.cwd() / "DQC Game Folders"
        
        self.active_search_game_btn = None
        self.active_search_path_btn = None
        self.active_gen_game_frame = None
        
        self.search_results = []
        self.selected_search_game = None
        self.selected_search_path = None
        self.path_labels = [] 
        
        self.generated_frames = []
        self.selected_generated_exe = None
        self.running_status_labels = {} 

        self._init_cache()
        self.setup_ui()
        self.refresh_exe_list()

        self.bind("<FocusIn>", self._on_focus_in)
        self.bind("<Return>", self._on_enter_pressed)
        self.bind("<Shift-Return>", self._on_shift_enter_pressed)
        self.bind("<Up>", self._on_up_down)
        self.bind("<Down>", self._on_up_down)
        self.bind("<Shift-Up>", self._on_shift_up_down)
        self.bind("<Shift-Down>", self._on_shift_up_down)
        
        self.after(100, lambda: self.search_entry.focus())

    def _on_enter_pressed(self, event):
        if "Search Mode" in self.tabview.get():
            self.create_and_run_from_search()
        elif "Manual Mode" in self.tabview.get():
            exe_path = self.create_exe_manual()
            if exe_path:
                self.run_game_by_path(exe_path)

    def _on_shift_enter_pressed(self, event):
        if "Search Mode" in self.tabview.get():
            self.create_from_search()

    def _on_up_down(self, event):
        if "Search Mode" not in self.tabview.get() or not self.search_results:
            return
        if not self.selected_search_game:
            return
        idx = next((i for i, g in enumerate(self.search_results) if g["name"] == self.selected_search_game), -1)
        if event.keysym == "Up":
            idx = max(0, idx - 1)
        elif event.keysym == "Down":
            idx = min(len(self.search_results) - 1, idx + 1)
        for child in self.games_scroll.winfo_children():
            if isinstance(child, ctk.CTkButton) and child.cget("text") == self.search_results[idx]["name"]:
                self._select_search_game(self.search_results[idx], child)
                break

    def _on_shift_up_down(self, event):
        if "Search Mode" not in self.tabview.get() or not self.selected_search_game:
            return
        game_data = next((g for g in self.search_results if g["name"] == self.selected_search_game), None)
        if not game_data or not game_data["paths"]: return
        idx = -1
        if self.selected_search_path in game_data["paths"]:
            idx = game_data["paths"].index(self.selected_search_path)
        if event.keysym == "Up":
            idx = max(0, idx - 1)
        elif event.keysym == "Down":
            idx = min(len(game_data["paths"]) - 1, idx + 1)
        new_path = game_data["paths"][idx]
        for lbl in self.path_labels:
            if lbl.cget("text") == new_path:
                frame = lbl.master
                self._select_search_path(new_path, frame)
                break

    def _init_cache(self):
        self.cache_file = Path.cwd() / "discord_cache.json"
        self.discord_cache = []

        if self.cache_file.exists():
            try:
                with open(self.cache_file, "r", encoding="utf-8") as f:
                    self.discord_cache = json.load(f)
            except Exception:
                pass

        threading.Thread(target=self._fetch_latest_cache, daemon=True).start()

    def _fetch_latest_cache(self):
        try:
            import requests
            resp = requests.get("https://discord.com/api/applications/detectable", timeout=15)
            if resp.status_code == 200:
                data = resp.json()
                self.discord_cache = data
                with open(self.cache_file, "w", encoding="utf-8") as f:
                    json.dump(data, f)
        except Exception:
            pass

    def setup_ui(self):
        self.grid_columnconfigure(0, weight=65, uniform="col") 
        self.grid_columnconfigure(1, weight=35, uniform="col")
        self.grid_rowconfigure(0, weight=1)

        # ==================== LEFT FRAME (Tabs) ====================
        left_frame = ctk.CTkFrame(self, fg_color="transparent")
        left_frame.grid(row=0, column=0, sticky="nsew", padx=(15, 10), pady=15)
        
        # Tab View
        self.tabview = ctk.CTkTabview(left_frame)
        self.tabview.pack(fill="both", expand=True)
        
        self.tab_auto = self.tabview.add("   Search Mode   ")
        self.tab_manual = self.tabview.add(" Manual Mode ")
        self.tabview.set("   Search Mode   ")

        # --- Search Mode TAB ---
        ctk.CTkLabel(self.tab_auto, text="Search", font=ctk.CTkFont(size=16, weight="bold")).pack(anchor="w", padx=10, pady=(5, 5))
        
        self.search_entry = ctk.CTkEntry(self.tab_auto, placeholder_text="Type a game name...")
        self.search_entry.pack(fill="x", padx=10, pady=(0, 10))
        self.search_entry.bind("<KeyRelease>", self._on_search_typing)

        lists_frame = ctk.CTkFrame(self.tab_auto, fg_color="transparent")
        lists_frame.pack(fill="both", expand=True, padx=10, pady=(0, 10))
        lists_frame.grid_columnconfigure(0, weight=1)
        lists_frame.grid_columnconfigure(1, weight=1)
        lists_frame.grid_rowconfigure(1, weight=1)

        ctk.CTkLabel(lists_frame, text="Select Game:", font=ctk.CTkFont(size=12, weight="bold")).grid(row=0, column=0, sticky="w", padx=0)
        self.games_scroll = ctk.CTkScrollableFrame(lists_frame)
        self.games_scroll.grid(row=1, column=0, sticky="nsew", padx=(0, 5))

        ctk.CTkLabel(lists_frame, text="Select Path:", font=ctk.CTkFont(size=12, weight="bold")).grid(row=0, column=1, sticky="w", padx=5)
        self.paths_scroll = ctk.CTkScrollableFrame(lists_frame)
        self.paths_scroll.grid(row=1, column=1, sticky="nsew", padx=(5, 0))
        
        self.paths_scroll.bind("<Configure>", self._on_paths_scroll_resize)

        search_action_frame = ctk.CTkFrame(self.tab_auto, fg_color="transparent")
        search_action_frame.pack(fill="x", padx=10, pady=(5, 10))
        
        self.edit_btn = ctk.CTkButton(search_action_frame, text="Edit in Manual Fields", command=self.copy_to_manual)
        self.edit_btn.pack(side="left", fill="x", expand=True, padx=(0, 5))
        
        self.create_search_btn = ctk.CTkButton(search_action_frame, text="Create Game (Shift + Enter)", command=self.create_from_search)
        self.create_search_btn.pack(side="left", fill="x", expand=True, padx=(5, 5))

        self.create_run_search_btn = ctk.CTkButton(search_action_frame, text="Create & Run (Enter)", command=self.create_and_run_from_search)
        self.create_run_search_btn.pack(side="left", fill="x", expand=True, padx=(5, 0))

        # --- Manual Mode TAB ---
        manual_header_frame = ctk.CTkFrame(self.tab_manual, fg_color="transparent")
        manual_header_frame.pack(fill="x", padx=10, pady=(15, 5))
        
        ctk.CTkLabel(manual_header_frame, text="Manual Mode", font=ctk.CTkFont(size=16, weight="bold")).pack(side="left")

        manual_inputs_frame = ctk.CTkFrame(self.tab_manual, fg_color="transparent")
        manual_inputs_frame.pack(fill="x", padx=10, pady=(10, 5))
        
        ctk.CTkLabel(manual_inputs_frame, text="Name (Optional):").grid(row=0, column=0, padx=(0, 10), pady=5, sticky="w")
        self.name_entry = ctk.CTkEntry(manual_inputs_frame)
        self.name_entry.grid(row=0, column=1, padx=0, pady=5, sticky="ew")
        
        ctk.CTkLabel(manual_inputs_frame, text="EXE Path:").grid(row=1, column=0, padx=(0, 10), pady=(0, 5), sticky="w")
        self.path_entry = ctk.CTkEntry(manual_inputs_frame)
        self.path_entry.grid(row=1, column=1, padx=0, pady=(0, 5), sticky="ew")
        
        manual_inputs_frame.grid_columnconfigure(1, weight=1)

        self.manual_create_btn = ctk.CTkButton(self.tab_manual, text="Create Game", command=self.create_exe_manual, fg_color="#28a745", hover_color="#218838", width=200)
        self.manual_create_btn.pack(pady=(10, 15))

        # Status Label (Placed below Tabs so it's always visible)
        self.status_label = ctk.CTkLabel(left_frame, text="Ready.", text_color="gray")
        self.status_label.pack(anchor="w", padx=10, pady=(5, 0))


        # ==================== RIGHT FRAME (Generated Games) ====================
        right_frame = ctk.CTkFrame(self, fg_color="transparent")
        right_frame.grid(row=0, column=1, sticky="nsew", padx=(5, 15), pady=15)
        
        ctk.CTkLabel(right_frame, text="Generated Games", font=ctk.CTkFont(size=16, weight="bold")).pack(anchor="w", pady=(0, 10))

        self.generated_scroll = ctk.CTkScrollableFrame(right_frame)
        self.generated_scroll.pack(fill="both", expand=True, pady=(0, 15))

        gen_action_frame = ctk.CTkFrame(right_frame, fg_color="transparent")
        gen_action_frame.pack(fill="x")
        
        self.run_btn = ctk.CTkButton(gen_action_frame, text="Run", command=self.run_game)
        self.run_btn.pack(side="left", fill="x", expand=True, padx=(0, 5))

        self.edit_gen_btn = ctk.CTkButton(gen_action_frame, text="Edit", command=self.edit_generated_game)
        self.edit_gen_btn.pack(side="left", fill="x", expand=True, padx=(5, 5))

        self.del_btn = ctk.CTkButton(gen_action_frame, text="Delete", command=self.delete_game)
        self.del_btn.pack(side="left", fill="x", expand=True, padx=(5, 0))

        # Initialize button states (Greyed out until selection)
        self._toggle_search_action_buttons(False)
        self._toggle_gen_action_buttons(False)

    # ==================== BUTTON COLOR STATES ====================
    def _set_btn_state(self, btn, is_active, fg_color, hover_color, text_color="white"):
        """Dynamically applies vibrant colors when active, and flat grey when disabled."""
        if is_active:
            btn.configure(state="normal", fg_color=fg_color, hover_color=hover_color, text_color=text_color)
        else:
            btn.configure(state="disabled", fg_color="gray25", text_color="gray55")

    def _toggle_search_action_buttons(self, state):
        self._set_btn_state(self.edit_btn, state, "#6c757d", "#5a6268")
        self._set_btn_state(self.create_search_btn, state, "#17a2b8", "#138496")
        self._set_btn_state(self.create_run_search_btn, state, "#28a745", "#218838")

    def _toggle_gen_action_buttons(self, state):
        self._set_btn_state(self.run_btn, state, "#007bff", "#0069d9")
        self._set_btn_state(self.edit_gen_btn, state, "#ffc107", "#e0a800", text_color="black" if state else "gray55")
        self._set_btn_state(self.del_btn, state, "#dc3545", "#c82333")

    # ==================== VALIDATION LOGIC ====================
    def is_valid_path(self, path_str):
        path_str = path_str.strip()
        if not path_str: return False, "Path cannot be empty."
        if "://" in path_str or "?" in path_str or "=" in path_str: return False, "URIs or web links are not valid paths."
        invalid_chars = r'[<>:"|?*]'
        if re.search(invalid_chars, path_str): return False, "Path contains invalid Windows characters (< > : \" | ? *)."
        if not path_str.lower().endswith(".exe"): return False, "Path must end with '.exe'."
        return True, "Valid"

    # ==================== SEARCH LOGIC ====================
    def normalize_string(self, text):
        if not text: return ""
        clean = re.sub(r'[^a-z0-9\s]', ' ', text.lower())
        return re.sub(r'\s+', ' ', clean).strip()

    def _on_search_typing(self, event):
        if event.keysym in ('Tab', 'Return', 'Left', 'Right', 'Up', 'Down', 'Shift_L', 'Shift_R', 'Control_L', 'Control_R', 'Alt_L', 'Alt_R'):
            return

        if hasattr(self, '_search_timer') and self._search_timer:
            self.after_cancel(self._search_timer)
            
        self._search_timer = self.after(500, self.perform_search)

    def perform_search(self):
        query = self.search_entry.get().strip()
        
        if len(query) < 3:
            for widget in self.games_scroll.winfo_children(): widget.destroy()
            for widget in self.paths_scroll.winfo_children(): widget.destroy()
            self.search_results.clear()
            self.selected_search_game = None
            self.selected_search_path = None
            self._toggle_search_action_buttons(False)
            
            if query:
                self._update_status("Type at least 3 characters to search...", "gray")
            else:
                self._update_status("Ready.", "gray")
            return

        self.status_label.configure(text=f"Searching Discord API for '{query}'...", text_color="orange")
        self._toggle_search_action_buttons(False)
        
        for widget in self.games_scroll.winfo_children(): widget.destroy()
        for widget in self.paths_scroll.winfo_children(): widget.destroy()
        
        self.search_results.clear()
        self.selected_search_game = None
        self.selected_search_path = None
        self.active_search_game_btn = None
        self.active_search_path_btn = None
        self.path_labels.clear()

        threading.Thread(target=self._fetch_discord_data, args=(query,), daemon=True).start()

    def _fetch_discord_data(self, query):
        try:
            if not self.discord_cache:
                import requests
                resp = requests.get("https://discord.com/api/applications/detectable", timeout=10)
                self.discord_cache = resp.json()
            
            query_norm = self.normalize_string(query)
            matches = []

            for app in self.discord_cache:
                name = app.get("name", "")
                aliases = app.get("aliases", [])
                
                search_targets = [name] + aliases
                matched = False
                
                for target in search_targets:
                    if query_norm in self.normalize_string(target):
                        matched = True
                        break

                if matched:
                    execs = app.get("executables", [])
                    valid_paths = [e["name"].replace("\\", "/") for e in execs if e.get("os") == "win32"]
                    
                    clean_paths = []
                    for p in valid_paths:
                        valid, _ = self.is_valid_path(p)
                        if valid: clean_paths.append(p)
                        elif not "://" in p and p.endswith(".exe"): 
                            clean_paths.append(p)

                    if clean_paths:
                        matches.append({"name": name, "norm_name": self.normalize_string(name), "paths": clean_paths})

            if matches:
                matches.sort(key=lambda x: abs(len(x["norm_name"]) - len(query_norm)))
                self.search_results = matches
                self.after(0, self._populate_games_list)
            else:
                self.after(0, lambda: self._update_status(f"Game '{query}' not found in Discord API.", "red"))

        except Exception as e:
            self.after(0, lambda: self._update_status("Please install 'requests' via terminal for live search.", "red"))

    def _populate_games_list(self):
        self._update_status("Search complete.", "green")
        
        first_btn = None
        for i, game_data in enumerate(self.search_results):
            btn = ctk.CTkButton(self.games_scroll, text=game_data["name"], anchor="w", fg_color="transparent", text_color=("gray10", "gray90"), hover_color=("gray70", "gray30"))
            btn.configure(command=lambda g=game_data, b=btn: self._select_search_game(g, b))
            btn.pack(fill="x", pady=2)
            if i == 0: first_btn = btn
            
        if self.search_results and first_btn:
            self._select_search_game(self.search_results[0], first_btn)

    def _select_search_game(self, game_data, active_btn):
        self.selected_search_game = game_data["name"]
        
        if self.active_search_game_btn and self.active_search_game_btn.winfo_exists():
            self.active_search_game_btn.configure(fg_color="transparent")
        
        active_btn.configure(fg_color=("#3b8ed0", "#1f538d"))
        self.active_search_game_btn = active_btn

        for widget in self.paths_scroll.winfo_children(): widget.destroy()
        self.active_search_path_btn = None
        self.path_labels.clear()
        
        first_frame = None
        for i, p in enumerate(game_data["paths"]):
            item_frame = ctk.CTkFrame(self.paths_scroll, border_width=1, border_color="gray30", fg_color="transparent", corner_radius=6)
            item_frame.pack(fill="x", pady=2, padx=2)
            
            lbl = ctk.CTkLabel(item_frame, text=p, justify="left", anchor="w", text_color=("gray10", "gray90"))
            lbl.pack(fill="x", padx=10, pady=5)
            
            for w in (item_frame, lbl):
                w.bind("<Button-1>", lambda e, p_val=p, f=item_frame: self._select_search_path(p_val, f))
                
            self.path_labels.append(lbl)
            if i == 0: first_frame = item_frame
            
        self.update_idletasks()
        self._apply_path_wraplength(self.paths_scroll.winfo_width())

        if game_data["paths"] and first_frame:
            self._select_search_path(game_data["paths"][0], first_frame)

    def _select_search_path(self, path_str, active_frame):
        self.selected_search_path = path_str
        if self.active_search_path_btn and self.active_search_path_btn.winfo_exists():
            self.active_search_path_btn.configure(fg_color="transparent")
        active_frame.configure(fg_color=("#3b8ed0", "#1f538d"))
        self.active_search_path_btn = active_frame
        
        # Now that a path is selected, enable the bottom buttons
        self._toggle_search_action_buttons(True)

    def _on_paths_scroll_resize(self, event):
        if hasattr(self, '_resize_timer') and self._resize_timer:
            self.after_cancel(self._resize_timer)
        self._resize_timer = self.after(150, lambda: self._apply_path_wraplength(event.width))

    def _apply_path_wraplength(self, width):
        target_wrap = max(50, width - 40)
        for lbl in self.path_labels:
            if lbl.winfo_exists():
                lbl.configure(wraplength=target_wrap)


    # ==================== CREATION LOGIC ====================
    def copy_to_manual(self):
        if not self.selected_search_game or not self.selected_search_path:
            return
        self.name_entry.delete(0, tk.END)
        self.name_entry.insert(0, self.selected_search_game)
        self.path_entry.delete(0, tk.END)
        self.path_entry.insert(0, self.selected_search_path)
        
        # Switch to Manual tab seamlessly
        self.tabview.set(" Manual Mode ")
        self._update_status("Copied to manual entry.", "green")

    def create_from_search(self):
        if not self.selected_search_game or not self.selected_search_path:
            return
        self._process_creation(self.selected_search_game, self.selected_search_path)

    def create_and_run_from_search(self):
        if not self.selected_search_game or not self.selected_search_path:
            return
        exe_path = self._process_creation(self.selected_search_game, self.selected_search_path)
        if exe_path:
            self.run_game_by_path(exe_path)

    def create_exe_manual(self):
        name = self.name_entry.get().strip()
        path = self.path_entry.get().strip()
        if not path:
            messagebox.showerror("Error", "Please enter an EXE Path manually.")
            return None
        return self._process_creation(name, path)

    def _process_creation(self, game_name, rel_path):
        if not rel_path.lower().endswith(".exe"): rel_path += ".exe"
            
        is_valid, msg = self.is_valid_path(rel_path)
        if not is_valid:
            messagebox.showerror("Invalid Path", f"The path provided is invalid:\n{msg}")
            return None
            
        exe_path = self.base_dir / rel_path
        self._update_status("Compiling...", "orange")
        self.update()

        success = self._compile_dummy_exe(exe_path, rel_path, game_name)
        if success:
            self._update_status(f"Success: {exe_path.name}", "green")
            self.refresh_exe_list(auto_select_path=exe_path)
            return exe_path
        else:
            self._update_status("Compilation failed.", "red")
            return None


    # ==================== COMPILER LOGIC ====================
    def _compile_dummy_exe(self, exe_path, rel_path, game_name):
        windir = os.environ.get('WINDIR', 'C:\\Windows')
        compiler_path = None
        for p in [Path(windir) / "Microsoft.NET" / "Framework64" / "v4.0.30319" / "csc.exe", 
                  Path(windir) / "Microsoft.NET" / "Framework" / "v4.0.30319" / "csc.exe"]:
            if p.exists():
                compiler_path = p
                break
                
        if not compiler_path:
            messagebox.showerror("Error", "Windows C# compiler (.NET Framework) missing.")
            return False

        try:
            target_dir = exe_path.parent
            cs_path = target_dir / f"{exe_path.stem}.cs"
            txt_path = target_dir / f"{exe_path.stem}.txt"

            target_dir.mkdir(parents=True, exist_ok=True)
            window_title = game_name if game_name else rel_path

            cs_code = f"""
            using System;
            using System.Windows.Forms;

            class DummyGame : Form {{
                public DummyGame() {{
                    this.Text = @"{window_title}";
                    this.Width = 600;
                    this.Height = 200;
                    this.StartPosition = FormStartPosition.CenterScreen;

                    Label lbl = new Label();
                    lbl.Text = "Target Path: " + @"{rel_path}" + "\\n\\nDummy game process is running.\\nKeep this window open to progress the quest.";
                    lbl.Top = 20;
                    lbl.Left = 20;
                    lbl.Width = 550;
                    lbl.Height = 120;

                    this.Controls.Add(lbl);
                }}

                [STAThread]
                static void Main() {{
                    Application.EnableVisualStyles();
                    Application.Run(new DummyGame());
                }}
            }}
            """
            with open(cs_path, 'w') as cs_file:
                cs_file.write(cs_code)

            compile_cmd = [str(compiler_path), "/nologo", "/target:winexe", "/r:System.Windows.Forms.dll", f"/out:{exe_path}", str(cs_path)]
            result = subprocess.run(compile_cmd, capture_output=True, text=True, creationflags=CREATE_NO_WINDOW)
            cs_path.unlink(missing_ok=True)

            if result.returncode == 0:
                if game_name:
                    with open(txt_path, 'w', encoding='utf-8') as txt_file: txt_file.write(game_name)
                else:
                    txt_path.unlink(missing_ok=True)
                return True
            else:
                messagebox.showerror("Compilation Error", f"Failed:\n{result.stderr}")
                return False
        except Exception as e:
            messagebox.showerror("Error", str(e))
            return False


    # ==================== GENERATED GAMES ====================
    def refresh_exe_list(self, auto_select_path=None):
        for widget in self.generated_scroll.winfo_children():
            widget.destroy()

        self.base_dir.mkdir(parents=True, exist_ok=True)
        exes = list(self.base_dir.rglob("*.exe"))

        self.selected_generated_exe = None
        self.active_gen_game_frame = None
        self.running_status_labels.clear()
        self._toggle_gen_action_buttons(False)

        if not exes:
            ctk.CTkLabel(self.generated_scroll, text="No executables found.\nCreate one from the left.", text_color="gray").pack(pady=20)
            return

        target_frame = None

        for exe_path in sorted(exes):
            rel_path = str(exe_path.relative_to(self.base_dir)).replace("\\", "/")
            
            txt_path = exe_path.parent / f"{exe_path.stem}.txt"
            game_name = ""
            if txt_path.exists():
                try: game_name = txt_path.read_text(encoding='utf-8').strip()
                except: pass

            display_name = game_name if game_name else "Unnamed Game"

            item_frame = ctk.CTkFrame(self.generated_scroll, border_width=1, border_color="gray30", fg_color="transparent", corner_radius=6)
            item_frame.pack(fill="x", pady=3, padx=2)
            
            name_row = ctk.CTkFrame(item_frame, fg_color="transparent")
            name_row.pack(fill="x", padx=10, pady=(5, 0))

            name_lbl = ctk.CTkLabel(name_row, text=display_name, font=ctk.CTkFont(size=14, weight="bold"), anchor="w", cursor="hand2")
            name_lbl.pack(side="left")
            
            status_lbl = ctk.CTkLabel(name_row, text="", font=ctk.CTkFont(size=12, weight="bold"))
            status_lbl.pack(side="left", padx=(10, 0))
            self.running_status_labels[exe_path] = status_lbl

            path_lbl = ctk.CTkLabel(item_frame, text=rel_path, font=ctk.CTkFont(size=11), text_color="gray60", anchor="w", cursor="hand2")
            path_lbl.pack(fill="x", padx=10, pady=(0, 5))

            for w in (item_frame, name_row, name_lbl, path_lbl, status_lbl):
                w.bind("<Button-1>", lambda e, p=exe_path, f=item_frame: self._select_generated_game(p, f))
            
            if auto_select_path and exe_path == auto_select_path:
                target_frame = item_frame

        if target_frame and auto_select_path:
            self._select_generated_game(auto_select_path, target_frame)
            
        self._trigger_status_update()

    def _select_generated_game(self, exe_path, active_frame):
        self.selected_generated_exe = exe_path
        
        if self.active_gen_game_frame and self.active_gen_game_frame.winfo_exists():
            self.active_gen_game_frame.configure(fg_color="transparent")
            
        active_frame.configure(fg_color=("#3b8ed0", "#1f538d"))
        self.active_gen_game_frame = active_frame
        
        self._toggle_gen_action_buttons(True)


    def run_game(self):
        if not self.selected_generated_exe or not self.selected_generated_exe.exists():
            messagebox.showerror("Error", "File not found.")
            self.refresh_exe_list()
            return
        self.run_game_by_path(self.selected_generated_exe)
        
    def run_game_by_path(self, exe_path):
        exe_name = exe_path.name
        try:
            cmd = ["tasklist", "/FI", f"IMAGENAME eq {exe_name}"]
            res = subprocess.run(cmd, capture_output=True, text=True, creationflags=CREATE_NO_WINDOW)
            if exe_name.lower() in res.stdout.lower():
                self._update_status(f"{exe_name} is already running.", "orange")
                return
        except: pass

        try:
            subprocess.Popen([str(exe_path)], creationflags=CREATE_NO_WINDOW)
            self._update_status(f"Started: {exe_name}", "green")
            self._trigger_status_update()
        except Exception as e:
            messagebox.showerror("Launch Error", str(e))

    def _on_focus_in(self, event):
        if str(event.widget) == ".":
            self._trigger_status_update()

    def _trigger_status_update(self):
        threading.Thread(target=self._check_running_statuses_thread, daemon=True).start()
        
    def _check_running_statuses_thread(self):
        try:
            res = subprocess.run(["tasklist"], capture_output=True, text=True, creationflags=CREATE_NO_WINDOW)
            running_tasks = res.stdout.lower()
            
            for exe_path, lbl in self.running_status_labels.items():
                if not lbl.winfo_exists(): continue
                if exe_path.name.lower() in running_tasks:
                    self.after(0, lambda l=lbl: l.configure(text="Running", text_color="#28a745"))
                else:
                    self.after(0, lambda l=lbl: l.configure(text=""))
        except:
            pass

    def edit_generated_game(self):
        if not self.selected_generated_exe: return
        
        old_exe = self.selected_generated_exe
        old_txt = old_exe.parent / f"{old_exe.stem}.txt"
        
        current_path = str(old_exe.relative_to(self.base_dir)).replace("\\", "/")
        current_name = ""
        if old_txt.exists():
            current_name = old_txt.read_text(encoding='utf-8').strip()

        dialog = EditGameDialog(self, current_name, current_path)
        self.wait_window(dialog)

        new_name = dialog.result_name
        new_path = dialog.result_path

        if new_path is not None and new_name is not None:
            if not new_path.lower().endswith(".exe"): new_path += ".exe"
            
            is_valid, msg = self.is_valid_path(new_path)
            if not is_valid:
                messagebox.showerror("Invalid Path", f"The new path is invalid:\n{msg}")
                return

            try:
                cmd = ["tasklist", "/FI", f"IMAGENAME eq {old_exe.name}"]
                res = subprocess.run(cmd, capture_output=True, text=True, creationflags=CREATE_NO_WINDOW)
                if old_exe.name.lower() in res.stdout.lower():
                    messagebox.showwarning("In Use", "Game is currently running. Close it before editing.")
                    return
            except: pass

            self._update_status("Applying edits...", "orange")
            self.update()

            new_exe_path = self.base_dir / new_path
            success = self._compile_dummy_exe(new_exe_path, new_path, new_name)
            
            if success:
                if old_exe != new_exe_path:
                    try: 
                        old_exe.unlink(missing_ok=True)
                        old_txt.unlink(missing_ok=True)
                    except: pass
                self._update_status("Game updated successfully.", "green")
                self.refresh_exe_list(auto_select_path=new_exe_path)
            else:
                self._update_status("Failed to apply edits.", "red")

    def delete_game(self):
        if not self.selected_generated_exe: return
        exe = self.selected_generated_exe
        txt = exe.parent / f"{exe.stem}.txt"

        if not messagebox.askyesno("Confirm Delete", f"Are you sure you want to delete this game?\n\n{exe.name}"):
            return

        try:
            cmd = ["tasklist", "/FI", f"IMAGENAME eq {exe.name}"]
            res = subprocess.run(cmd, capture_output=True, text=True, creationflags=CREATE_NO_WINDOW)
            if exe.name.lower() in res.stdout.lower():
                messagebox.showwarning("In Use", "Game is running. Close it before deleting.")
                return
        except: pass

        try:
            exe.unlink(missing_ok=True)
            txt.unlink(missing_ok=True)
            self._update_status(f"Deleted: {exe.name}", "gray")
            self.refresh_exe_list()
        except Exception as e:
            messagebox.showerror("Error", f"Failed to delete:\n{e}")

    # ==================== UTILS ====================
    def _update_status(self, msg, color):
        if color == "green": c = "#28a745"
        elif color == "red": c = "#dc3545"
        elif color == "orange": c = "#ffc107"
        else: c = "gray"
        self.status_label.configure(text=msg, text_color=c)


if __name__ == "__main__":
    app = DummyExeApp()
    app.mainloop()
