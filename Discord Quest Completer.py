import os
import subprocess
import tkinter as tk
from tkinter import messagebox, simpledialog
from pathlib import Path
import threading
import webbrowser
import urllib.parse
import json

# Windows constant to prevent subprocess from flashing a black CMD window
CREATE_NO_WINDOW = 0x08000000

class ToolTip:
    """A simple ToolTip class for Tkinter widgets."""
    def __init__(self, widget, text):
        self.widget = widget
        self.text = text
        self.tipwindow = None
        self.id = None
        self.widget.bind("<Enter>", self.enter)
        self.widget.bind("<Leave>", self.leave)

    def enter(self, event=None):
        self.schedule()

    def leave(self, event=None):
        self.unschedule()
        self.hidetip()

    def schedule(self):
        self.unschedule()
        self.id = self.widget.after(400, self.showtip)

    def unschedule(self):
        id = self.id
        self.id = None
        if id:
            self.widget.after_cancel(id)

    def showtip(self, event=None):
        # Calculate exactly where the widget is on the screen
        x = self.widget.winfo_rootx() + 20
        y = self.widget.winfo_rooty() + self.widget.winfo_height() + 5
        
        self.tipwindow = tw = tk.Toplevel(self.widget)
        tw.wm_overrideredirect(True)
        tw.wm_geometry("+%d+%d" % (x, y))
        label = tk.Label(tw, text=self.text, justify=tk.LEFT,
                         background="#ffffe0", relief=tk.SOLID, borderwidth=1,
                         font=("tahoma", "9", "normal"))
        label.pack(ipadx=1)

    def hidetip(self):
        tw = self.tipwindow
        self.tipwindow = None
        if tw:
            tw.destroy()


class DummyExeApp:
    def __init__(self, root):
        self.root = root
        self.root.title("Discord Quest Completer")
        # Slightly wider default width to comfortably fit the side-by-side layout
        self.root.geometry("800x780")
        self.root.minsize(700, 700)

        self.base_dir = Path.cwd() / "DQC Game Folders"

        # --- Mouse Scroll Binding (Cross-Platform) ---
        self.root.bind_all("<MouseWheel>", self._on_mousewheel)
        self.root.bind_all("<Button-4>", self._on_mousewheel)
        self.root.bind_all("<Button-5>", self._on_mousewheel)

        # --- USAGE GUIDE (Pinned to bottom) ---
        guide_frame = tk.Frame(root, pady=10, padx=10, relief=tk.SUNKEN, bd=1)
        guide_frame.pack(side=tk.BOTTOM, fill=tk.X)
        
        guide_text = (
            "Steps to Use:\n"
            "1. Fill in the EXE Path field with the proper path structure of the game.\n"
            "   e.g., Where Winds Meet/Engine/Binaries/Win64/wwm.exe\n"
            "2. Click 'Create Executable' to generate a dummy game.\n"
            "3. Click 'Start' from your list of games and the quest should start progressing.\n\n"
            "Tips:\n"
            "1. Use 'Search and fill' to auto-find paths and fill the fields. (Check more info from that section)\n"
            "2. Set a Name to identify games easily and display it in the running window.\n"
            "3. For help with finding paths, we suggest this subreddit: (This app is not affiliated with the subreddit)"
        )
        tk.Label(guide_frame, text=guide_text, justify=tk.LEFT, font=("Arial", 9)).pack(side=tk.LEFT)
        
        reddit_main_link = tk.Label(guide_frame, text="r/DiscordQuests", fg="blue", cursor="hand2", font=("Arial", 9, "underline"))
        reddit_main_link.pack(side=tk.LEFT, anchor=tk.S, pady=(0, 2))
        reddit_main_link.bind("<Button-1>", lambda e: webbrowser.open("https://www.reddit.com/r/DiscordQuests/"))

        # --- TOP SECTION: CREATE ---
        top_frame = tk.Frame(root, pady=10, padx=10)
        top_frame.pack(side=tk.TOP, fill=tk.X)

        input_frame = tk.Frame(top_frame)
        input_frame.pack(fill=tk.X, expand=True)

        # Labels and Entries Grid
        tk.Label(input_frame, text="Name (Optional):", font=("Arial", 9, "bold")).grid(row=0, column=0, sticky=tk.W, pady=(0, 5))
        self.name_entry = tk.Entry(input_frame)
        self.name_entry.grid(row=0, column=1, sticky="ew", padx=(5, 10), pady=(0, 5))

        tk.Label(input_frame, text="EXE Path:", font=("Arial", 9, "bold")).grid(row=1, column=0, sticky=tk.W)
        self.path_entry = tk.Entry(input_frame)
        self.path_entry.insert(0, "")
        self.path_entry.grid(row=1, column=1, sticky="ew", padx=(5, 10))

        # Create Button
        self.create_btn = tk.Button(input_frame, text="Create Executable", command=self.create_exe, width=15, bg="#d4edda")
        self.create_btn.grid(row=0, column=2, rowspan=2, sticky="nsew")

        input_frame.columnconfigure(1, weight=1)

        self.status_var = tk.StringVar()
        self.status_var.set("Ready.")
        self.status_label = tk.Label(top_frame, textvariable=self.status_var, fg="blue")
        self.status_label.pack(anchor=tk.W, pady=(10, 0))

        # --- Horizontal separator 1 ---
        tk.Frame(top_frame, height=2, bd=1, relief=tk.SUNKEN).pack(fill=tk.X, pady=(10, 8))

        # --- MIDDLE SPLIT SECTION (Search Left | DB Right) ---
        split_frame = tk.Frame(top_frame)
        split_frame.pack(fill=tk.X, pady=5)

        # Define the width ratio (e.g., 2 to 1 means the left side gets 66% of the space)
        split_frame.columnconfigure(0, weight=4) # Left side weight
        split_frame.columnconfigure(2, weight=1) # Right side weight

        # LEFT SIDE (Search)
        left_search_frame = tk.Frame(split_frame)
        left_search_frame.grid(row=0, column=0, sticky="nsew", padx=(0, 10))

        # Vertical separator between the two halves
        tk.Frame(split_frame, width=2, bd=1, relief=tk.SUNKEN).grid(row=0, column=1, sticky="ns", padx=5)

        # RIGHT SIDE (Local DB)
        right_db_frame = tk.Frame(split_frame)
        right_db_frame.grid(row=0, column=2, sticky="nsew", padx=(10, 0))

        # --- LEFT: Search & Auto Fill ---
        search_header_frame = tk.Frame(left_search_frame)
        search_header_frame.pack(fill=tk.X, pady=(0, 2))
        
        tk.Label(search_header_frame, text="Search and auto fill fields:", font=("Arial", 9, "bold")).pack(side=tk.LEFT)
        search_tooltip = ("Type a game name below to find its exe path\n"
                          "1. It checks your local 'game-index.json' file or queries Steam API.\n"
                          "2. If found, it auto-fills the Name and EXE Path fields above.\n\n"
                          "If not found or the path is incorrect, you can search on Google or the subreddit using the links below\n"
                          "or update your local database in the section to the right.")
        self.create_info_icon(search_header_frame, search_tooltip).pack(side=tk.LEFT, padx=5)

        steamdb_frame = tk.Frame(left_search_frame, pady=5)
        steamdb_frame.pack(fill=tk.X)

        self.search_var = tk.StringVar()
        self.search_var.trace_add("write", self.update_search_links)
        
        self.steamdb_entry = tk.Entry(steamdb_frame, textvariable=self.search_var)
        self.steamdb_entry.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(0, 10))

        self.steamdb_btn = tk.Button(steamdb_frame, text="Search and fill", command=self.search_and_fill, width=15, bg="#cce5ff")
        self.steamdb_btn.pack(side=tk.LEFT)

        links_frame = tk.Frame(left_search_frame)
        links_frame.pack(fill=tk.X, pady=(0, 5))

        self.reddit_search_link = tk.Label(links_frame, text="", fg="blue", cursor="hand2", font=("Arial", 8, "underline"))
        self.reddit_search_link.pack(side=tk.LEFT, padx=(0, 15))
        self.reddit_search_link.bind("<Button-1>", lambda e: self.open_custom_search("reddit"))

        self.google_search_link = tk.Label(links_frame, text="", fg="blue", cursor="hand2", font=("Arial", 8, "underline"))
        self.google_search_link.pack(side=tk.LEFT)
        self.google_search_link.bind("<Button-1>", lambda e: self.open_custom_search("google"))

        self.update_search_links()

        # --- RIGHT: Update Local Database ---
        localdb_header_frame = tk.Frame(right_db_frame)
        localdb_header_frame.pack(fill=tk.X, pady=(0, 2))
        
        tk.Label(localdb_header_frame, text="Update Local Database:", font=("Arial", 9, "bold")).pack(side=tk.LEFT)
        db_tooltip = ("How to update your local game database:\n"
                      "1. The link below takes you to r/DiscordQuests/wiki/game-index.\n"
                      "2. After opening the page, press Ctrl + S to save the file.\n"
                      "3. Save the file directly into this App's Directory.\n"
                      "   (Use the 'Open App Directory' button to find it easily)\n\n"
                      "This data is collected from a community maintained Subreddit.\n"
                      "You have to redownload it from time to time to get the latest updates.")
        self.create_info_icon(localdb_header_frame, db_tooltip).pack(side=tk.LEFT, padx=5)

        # Container for Link and Button (taking 2 lines)
        localdb_tools_frame = tk.Frame(right_db_frame)
        localdb_tools_frame.pack(fill=tk.X, pady=(5, 0))

        json_link = tk.Label(localdb_tools_frame, text="Download latest paths database", fg="blue", cursor="hand2", font=("Arial", 8, "underline"))
        json_link.pack(anchor=tk.W, pady=(0, 5))
        json_link.bind("<Button-1>", lambda e: webbrowser.open("https://www.reddit.com/r/DiscordQuests/wiki/game-index.json"))

        open_folder_btn = tk.Button(localdb_tools_frame, text="Open App Directory", command=self.open_app_folder, font=("Arial", 8), bg="#e2e3e5")
        open_folder_btn.pack(anchor=tk.W)

        # --- Horizontal separator 3 ---
        tk.Frame(top_frame, height=2, bd=1, relief=tk.SUNKEN).pack(fill=tk.X, pady=(15, 0))

        # --- BOTTOM SECTION: DYNAMIC LIST ---
        tk.Label(root, text="Generated Executables:", font=("Arial", 10, "bold")).pack(side=tk.TOP, anchor=tk.W, padx=10, pady=(5, 0))

        list_container = tk.Frame(root)
        list_container.pack(side=tk.TOP, fill=tk.BOTH, expand=True, padx=10, pady=(5, 10))

        self.canvas = tk.Canvas(list_container, highlightthickness=0)
        scrollbar = tk.Scrollbar(list_container, orient="vertical", command=self.canvas.yview)
        self.scrollable_frame = tk.Frame(self.canvas)

        self.scrollable_frame.bind(
            "<Configure>",
            lambda e: self.canvas.configure(scrollregion=self.canvas.bbox("all"))
        )

        self.canvas.create_window((0, 0), window=self.scrollable_frame, anchor="nw")
        self.canvas.configure(yscrollcommand=scrollbar.set)

        self.canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)

        self.refresh_exe_list()

    def create_info_icon(self, parent, tooltip_text):
        """Creates a circular '?' icon and attaches a tooltip to it."""
        canvas = tk.Canvas(parent, width=16, height=16, highlightthickness=0)
        # Draw a circle
        canvas.create_oval(1, 1, 15, 15, outline="#0056b3", width=1.5)
        # Draw the question mark inside
        canvas.create_text(8, 8, text="?", fill="#0056b3", font=("Arial", 8, "bold"))
        # Attach our custom ToolTip
        ToolTip(canvas, tooltip_text)
        return canvas

    def _on_mousewheel(self, event):
        """Cross-platform mouse wheel scrolling logic for the canvas."""
        if hasattr(event, "delta") and event.delta != 0:
            direction = -1 if event.delta > 0 else 1
            self.canvas.yview_scroll(direction, "units")
        elif hasattr(event, "num"):
            if event.num == 4:
                self.canvas.yview_scroll(-1, "units")
            elif event.num == 5:
                self.canvas.yview_scroll(1, "units")

    def update_search_links(self, *args):
        self.reddit_search_link.config(text=f"Search for your keywords on subreddit")
        self.google_search_link.config(text=f"Search for your keywords on Google")

    def open_custom_search(self, target):
        query = self.search_var.get().strip()
        if not query:
            messagebox.showinfo("Empty Search", "Please type a game name in the search field first.")
            return

        if target == "reddit":
            safe_query = urllib.parse.quote(query)
            url = f"https://www.reddit.com/r/DiscordQuests/search/?q={safe_query}"
            webbrowser.open(url)
        elif target == "google":
            safe_query = urllib.parse.quote(f"{query} exe path")
            url = f"https://www.google.com/search?q={safe_query}"
            webbrowser.open(url)

    def open_app_folder(self):
        """Opens the current working directory in the Windows File Explorer."""
        try:
            os.startfile(str(Path.cwd()))
        except Exception as e:
            messagebox.showerror("Error", f"Could not open directory:\n{e}")

    def load_local_db(self):
        """Parses the Reddit wiki JSON file if it exists in the app directory."""
        db_path = Path.cwd() / "game-index.json"
        local_games = []
        if db_path.exists():
            try:
                with open(db_path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    md = data.get("data", {}).get("content_md", "")
                    lines = [line.strip() for line in md.split('\n') if line.strip()]
                    
                    for i in range(1, len(lines)):
                        # Look for lines formatted as `..\Path\exe`
                        if lines[i].startswith('`') and lines[i].endswith('`'):
                            # The preceding line is the game name
                            g_name = lines[i-1].replace("  ", "").strip() 
                            g_path = lines[i].strip('`')
                            
                            # Clean up the path format
                            if g_path.startswith("..\\") or g_path.startswith("../"):
                                g_path = g_path[3:]
                            g_path = g_path.replace("\\", "/")
                            
                            local_games.append({"name": g_name, "path": g_path})
            except Exception as e:
                print(f"Failed to parse game-index.json: {e}")
        return local_games

    def search_and_fill(self):
        game_query = self.steamdb_entry.get().strip()
        if not game_query:
            messagebox.showinfo("Input Needed", "Please type a game name in the search field above.")
            return

        # 1. Search the Local Database First
        local_db = self.load_local_db()
        best_match = None

        # Check for exact match
        for game in local_db:
            if game['name'].lower() == game_query.lower():
                best_match = game
                break
        
        # Check for substring match if no exact match
        if not best_match:
            matches = [g for g in local_db if game_query.lower() in g['name'].lower()]
            if matches:
                # Pick the closest match by string length
                matches.sort(key=lambda x: abs(len(x["name"]) - len(game_query)))
                best_match = matches[0]

        if best_match:
            # Match found locally! Update instantly.
            self.name_entry.delete(0, tk.END)
            self.name_entry.insert(0, best_match["name"])
            self.path_entry.delete(0, tk.END)
            self.path_entry.insert(0, best_match["path"])
            self.status_var.set(f"Found '{best_match['name']}' in local game-index.json!")
            self.status_label.config(fg="green")
            return

        # 2. Not found locally, fallback to Steam API
        self.steamdb_btn.config(state=tk.DISABLED)
        self.status_var.set(f"Not found locally. Searching Steam for '{game_query}'...")
        self.status_label.config(fg="orange")
        self.root.update()

        def fetch_task():
            try:
                import requests
                from steam.client import SteamClient
            except ImportError:
                def show_import_error():
                    messagebox.showerror("Missing Libraries",
                        "To use Steam Search, you must install the required libraries.\n\n"
                        "Open Command Prompt and run:\n"
                        "pip install requests steam")
                    self.status_var.set("Missing required python packages.")
                    self.status_label.config(fg="red")
                    self.steamdb_btn.config(state=tk.NORMAL)
                self.root.after(0, show_import_error)
                return

            try:
                search_url = f'https://store.steampowered.com/api/storesearch/?term={game_query}&l=english&cc=US'
                search_resp = requests.get(search_url, timeout=10).json()

                if not search_resp.get('items'):
                    self.root.after(0, lambda: self.status_var.set(f"Game '{game_query}' not found on Steam."))
                    self.root.after(0, lambda: self.status_label.config(fg="red"))
                    self.root.after(0, lambda: self.steamdb_btn.config(state=tk.NORMAL))
                    return

                game_data = search_resp['items'][0]
                app_id = game_data['id']
                official_name = game_data['name']

                self.root.after(0, lambda: self.status_var.set(f"Found '{official_name}'. Contacting Steam servers for path..."))

                client = SteamClient()
                try:
                    client.anonymous_login()
                    product_info = client.get_product_info(apps=[app_id])

                    if not product_info or 'apps' not in product_info:
                        raise Exception("Steam network failed to return product info.")

                    app_config = product_info['apps'][app_id].get('config', {})
                    install_dir = app_config.get('installdir', official_name)
                    launch_data = app_config.get('launch', {})
                    exe_rel_path = 'unknown.exe'

                    if launch_data:
                        first_launch = list(launch_data.values())[0]
                        exe_rel_path = first_launch.get('executable', 'unknown.exe')

                    exe_rel_path = exe_rel_path.replace("\\\\", "/").replace("\\", "/")
                    final_path = f"common/{install_dir}/{exe_rel_path}"

                    def update_ui_success():
                        self.name_entry.delete(0, tk.END)
                        self.name_entry.insert(0, official_name)
                        self.path_entry.delete(0, tk.END)
                        self.path_entry.insert(0, final_path)
                        self.status_var.set(f"Successfully loaded Steam path for '{official_name}'")
                        self.status_label.config(fg="green")
                        self.steamdb_btn.config(state=tk.NORMAL)

                    self.root.after(0, update_ui_success)

                finally:
                    client.disconnect()

            except Exception as e:
                def update_ui_err():
                    self.status_var.set("Error while fetching data.")
                    self.status_label.config(fg="red")
                    self.steamdb_btn.config(state=tk.NORMAL)
                    messagebox.showerror("Network Error", f"Could not fetch data:\n{str(e)}")
                self.root.after(0, update_ui_err)

        threading.Thread(target=fetch_task, daemon=True).start()

    def find_builtin_compiler(self):
        windir = os.environ.get('WINDIR', 'C:\\Windows')
        possible_paths = [
            Path(windir) / "Microsoft.NET" / "Framework64" / "v4.0.30319" / "csc.exe",
            Path(windir) / "Microsoft.NET" / "Framework" / "v4.0.30319" / "csc.exe"
        ]
        for p in possible_paths:
            if p.exists():
                return p
        return None

    def _compile_dummy_exe(self, exe_path, rel_path, game_name):
        csc_compiler = self.find_builtin_compiler()
        if not csc_compiler:
            messagebox.showerror("Error", "Could not find the built-in Windows C# compiler (.NET Framework).")
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

            compile_cmd = [
                str(csc_compiler),
                "/nologo",
                "/target:winexe",
                "/r:System.Windows.Forms.dll",
                f"/out:{exe_path}",
                str(cs_path)
            ]

            result = subprocess.run(compile_cmd, capture_output=True, text=True, creationflags=CREATE_NO_WINDOW)

            cs_path.unlink(missing_ok=True)

            if result.returncode == 0:
                if game_name:
                    with open(txt_path, 'w', encoding='utf-8') as txt_file:
                        txt_file.write(game_name)
                else:
                    txt_path.unlink(missing_ok=True)
                return True
            else:
                messagebox.showerror("Compilation Error", f"Failed:\n{result.stderr}")
                return False

        except Exception as e:
            messagebox.showerror("Error", str(e))
            return False

    def create_exe(self):
        rel_path = self.path_entry.get().strip()
        game_name = self.name_entry.get().strip()

        if not rel_path:
            messagebox.showerror("Input Error", "The path cannot be empty.")
            return

        if not rel_path.lower().endswith(".exe"):
            rel_path += ".exe"
            self.path_entry.delete(0, tk.END)
            self.path_entry.insert(0, rel_path)

        exe_path = self.base_dir / rel_path

        self.status_var.set("Compiling...")
        self.root.update()

        success = self._compile_dummy_exe(exe_path, rel_path, game_name)
        if success:
            self.status_var.set(f"Successfully created: {exe_path.name}")
            self.status_label.config(fg="green")
            self.name_entry.delete(0, tk.END)
            self.refresh_exe_list()
        else:
            self.status_var.set("Compilation failed.")
            self.status_label.config(fg="red")

    def refresh_exe_list(self):
        for widget in self.scrollable_frame.winfo_children():
            widget.destroy()

        self.base_dir.mkdir(parents=True, exist_ok=True)
        exes = list(self.base_dir.rglob("*.exe"))

        if not exes:
            tk.Label(self.scrollable_frame, text="No executables found. Create one above.", fg="grey").pack(pady=10)
            return

        for exe_path in sorted(exes):
            self.create_list_item(exe_path)

    def create_list_item(self, exe_path):
        row_frame = tk.Frame(self.scrollable_frame, bd=1, relief=tk.SOLID)
        row_frame.pack(fill=tk.X, pady=3, padx=2)

        rel_path_str = str(exe_path.relative_to(self.base_dir)).replace("\\", "/")

        txt_path = exe_path.parent / f"{exe_path.stem}.txt"
        game_name = ""
        if txt_path.exists():
            try:
                game_name = txt_path.read_text(encoding='utf-8').strip()
            except Exception:
                pass

        display_text = game_name if game_name else rel_path_str

        name_label = tk.Label(row_frame, text=display_text, anchor=tk.W, font=("Courier", 10, "bold" if game_name else "normal"))
        name_label.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=5, pady=5)

        ToolTip(name_label, f"Path: {rel_path_str}")

        btn_start = tk.Button(row_frame, text="Start", width=8, bg="#cce5ff",
                              command=lambda p=exe_path: self.start_exe(p))
        btn_start.pack(side=tk.LEFT, padx=2, pady=5)

        btn_rename = tk.Button(row_frame, text="Rename", width=8, bg="#e2e3e5",
                               command=lambda p=exe_path, n=game_name, r=rel_path_str: self.rename_exe(p, n, r))
        btn_rename.pack(side=tk.LEFT, padx=2, pady=5)

        btn_del = tk.Button(row_frame, text="Delete", width=8, bg="#f8d7da",
                            command=lambda p=exe_path, t=txt_path: self.delete_exe(p, t))
        btn_del.pack(side=tk.LEFT, padx=(2, 5), pady=5)

    def is_running(self, exe_name):
        try:
            cmd = ["tasklist", "/FI", f"IMAGENAME eq {exe_name}"]
            result = subprocess.run(cmd, capture_output=True, text=True, creationflags=CREATE_NO_WINDOW)
            return exe_name.lower() in result.stdout.lower()
        except Exception:
            return False

    def start_exe(self, exe_path):
        if not exe_path.exists():
            messagebox.showerror("Error", "File not found.")
            self.refresh_exe_list()
            return

        exe_name = exe_path.name
        if self.is_running(exe_name):
            self.status_var.set(f"{exe_name} is already running.")
            self.status_label.config(fg="orange")
        else:
            try:
                subprocess.Popen([str(exe_path)], creationflags=CREATE_NO_WINDOW)
                self.status_var.set(f"Started: {exe_name}")
                self.status_label.config(fg="green")
            except Exception as e:
                messagebox.showerror("Launch Error", str(e))

    def rename_exe(self, exe_path, current_name, rel_path_str):
        if self.is_running(exe_path.name):
            messagebox.showwarning("In Use", f"'{exe_path.name}' is currently running.\nPlease close its window before renaming it.")
            return

        new_name = simpledialog.askstring("Rename / Add Name",
                                          "Enter name for this executable\n(Leave blank to revert to path):",
                                          initialvalue=current_name)

        if new_name is not None:
            new_name = new_name.strip()
            self.status_var.set("Recompiling with new name...")
            self.root.update()

            success = self._compile_dummy_exe(exe_path, rel_path_str, new_name)

            if success:
                self.status_var.set(f"Successfully updated: {exe_path.name}")
                self.status_label.config(fg="green")
                self.refresh_exe_list()
            else:
                self.status_var.set("Failed to rename/recompile.")
                self.status_label.config(fg="red")

    def delete_exe(self, exe_path, txt_path):
        if self.is_running(exe_path.name):
            messagebox.showwarning("In Use", f"'{exe_path.name}' is currently running.\nPlease close its window before deleting it.")
            return
        try:
            if exe_path.exists():
                exe_path.unlink()
            if txt_path.exists():
                txt_path.unlink()

            self.status_var.set(f"Deleted: {exe_path.name}")
            self.status_label.config(fg="black")
            self.refresh_exe_list()
        except PermissionError:
            messagebox.showerror("Permission Error", "File is locked. Make sure you closed the game window.")
        except Exception as e:
            messagebox.showerror("Delete Error", str(e))


if __name__ == "__main__":
    root = tk.Tk()
    app = DummyExeApp(root)
    root.mainloop()