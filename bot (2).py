import sqlite3
import asyncio
import random
import time
import os
import io
import subprocess
import json
import zipfile
import shutil
import tempfile
import base64
import re
from datetime import datetime, timedelta
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np
from aiogram import Bot, Dispatcher, executor, types
from aiogram.dispatcher.middlewares import BaseMiddleware
from aiogram.dispatcher.handler import CancelHandler
from aiogram.types import InlineKeyboardMarkup, InlineKeyboardButton
from aiogram.dispatcher import FSMContext
from aiogram.dispatcher.filters.state import State, StatesGroup
from aiogram.contrib.fsm_storage.memory import MemoryStorage

BOT_TOKEN = os.getenv("BOT_TOKEN", "").strip()
if ":" in BOT_TOKEN and len(BOT_TOKEN.split(":")) >= 2:
    parts = BOT_TOKEN.split(":")
    if len(parts) == 3:
        BOT_TOKEN = ":".join(parts[-2:])
    elif len(parts) == 2:
        BOT_TOKEN = BOT_TOKEN
BOT_TOKEN = BOT_TOKEN.strip()
if not BOT_TOKEN:
    raise ValueError("BOT_TOKEN is not set in environment variables")
DB_PATH = os.getenv("DB_PATH", "ecsp_guard_bot.db")

START_BALANCE = int(os.getenv("START_BALANCE", "50"))
DUEL_BASE_XP_WIN = int(os.getenv("DUEL_BASE_XP_WIN", "15"))
DUEL_BASE_XP_LOSE = int(os.getenv("DUEL_BASE_XP_LOSE", "5"))
LEVEL_XP = int(os.getenv("LEVEL_XP", "100"))
ADMINS = set(map(int, os.getenv("ADMIN_IDS", "7587362459").split(",")))

storage = MemoryStorage()
bot = Bot(token=BOT_TOKEN)
dp = Dispatcher(bot, storage=storage)

conn = sqlite3.connect(DB_PATH, check_same_thread=False)
cursor = conn.cursor()

duel_requests = {}
ongoing_duel = None
spectator_bets = {}
tictactoe_games = {}
guess_number_games = {}
rps_requests = {}
quiz_sessions = {}
mafia_rooms = {}
raid_rooms = {}
boss_raid_rooms = {}
darkness_rooms_active = {}
hangman_games = {}
blackjack_games = {}
word_chain_games = {}
memory_games = {}
chat_locks = {}
code_compile_sessions = {}

async def get_chat_lock(chat_id):
    if chat_id not in chat_locks:
        chat_locks[chat_id] = asyncio.Lock()
    return chat_locks[chat_id]

class QuizCreation(StatesGroup):
    waiting_for_name = State()
    waiting_for_question = State()
    waiting_for_answers = State()
    waiting_for_correct = State()
    adding_more = State()

class CodeExecution(StatesGroup):
    waiting_for_code = State()
    waiting_for_language = State()

def init_db():
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS users(
        user_id INTEGER PRIMARY KEY,
        username TEXT,
        balance INTEGER DEFAULT 0,
        xp INTEGER DEFAULT 0,
        level INTEGER DEFAULT 1,
        wins INTEGER DEFAULT 0,
        losses INTEGER DEFAULT 0,
        games_played INTEGER DEFAULT 0,
        last_daily INTEGER DEFAULT 0
    )""")

    cursor.execute("""
    CREATE TABLE IF NOT EXISTS items(
        item_id INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT,
        type TEXT,
        power INTEGER,
        price INTEGER
    )""")

    cursor.execute("""
    CREATE TABLE IF NOT EXISTS inventory(
        user_id INTEGER,
        item_id INTEGER,
        qty INTEGER DEFAULT 1,
        PRIMARY KEY(user_id, item_id),
        FOREIGN KEY(user_id) REFERENCES users(user_id),
        FOREIGN KEY(item_id) REFERENCES items(item_id)
    )""")

    cursor.execute("""
    CREATE TABLE IF NOT EXISTS loans(
        loan_id INTEGER PRIMARY KEY AUTOINCREMENT,
        user_id INTEGER,
        amount INTEGER,
        created_at INTEGER,
        FOREIGN KEY(user_id) REFERENCES users(user_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS raids(
        raid_id INTEGER PRIMARY KEY,
        target_user_id INTEGER,
        creator_id INTEGER,
        reward INTEGER,
        created_at INTEGER,
        status TEXT DEFAULT 'active',
        chat_id INTEGER,
        FOREIGN KEY(target_user_id) REFERENCES users(user_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS raid_participants(
        raid_id INTEGER,
        user_id INTEGER,
        joined_at INTEGER,
        PRIMARY KEY(raid_id, user_id),
        FOREIGN KEY(raid_id) REFERENCES raids(raid_id),
        FOREIGN KEY(user_id) REFERENCES users(user_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS bosses(
        boss_id INTEGER PRIMARY KEY,
        name TEXT,
        hp INTEGER,
        max_hp INTEGER,
        damage INTEGER,
        defense INTEGER,
        entry_fee INTEGER,
        drop_reward INTEGER,
        created_by INTEGER,
        status TEXT DEFAULT 'alive'
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS boss_raids(
        boss_raid_id INTEGER PRIMARY KEY AUTOINCREMENT,
        boss_id INTEGER,
        creator_id INTEGER,
        chat_id INTEGER,
        created_at INTEGER,
        status TEXT DEFAULT 'active',
        FOREIGN KEY(boss_id) REFERENCES bosses(boss_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS boss_raid_participants(
        boss_raid_id INTEGER,
        user_id INTEGER,
        damage_dealt INTEGER DEFAULT 0,
        PRIMARY KEY(boss_raid_id, user_id),
        FOREIGN KEY(boss_raid_id) REFERENCES boss_raids(boss_raid_id),
        FOREIGN KEY(user_id) REFERENCES users(user_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS boss_drops(
        drop_id INTEGER PRIMARY KEY AUTOINCREMENT,
        boss_id INTEGER,
        item_id INTEGER,
        drop_chance INTEGER DEFAULT 50,
        FOREIGN KEY(boss_id) REFERENCES bosses(boss_id),
        FOREIGN KEY(item_id) REFERENCES items(item_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS loot_boxes(
        box_id INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT,
        price INTEGER,
        created_by INTEGER,
        created_at INTEGER
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS loot_box_items(
        box_id INTEGER,
        item_id INTEGER,
        drop_chance INTEGER DEFAULT 50,
        PRIMARY KEY(box_id, item_id),
        FOREIGN KEY(box_id) REFERENCES loot_boxes(box_id),
        FOREIGN KEY(item_id) REFERENCES items(item_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS clans(
        clan_id INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT UNIQUE NOT NULL,
        owner_id INTEGER NOT NULL,
        balance INTEGER DEFAULT 0,
        created_at INTEGER NOT NULL,
        FOREIGN KEY(owner_id) REFERENCES users(user_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS clan_members(
        clan_id INTEGER,
        user_id INTEGER,
        joined_at INTEGER NOT NULL,
        PRIMARY KEY(clan_id, user_id),
        FOREIGN KEY(clan_id) REFERENCES clans(clan_id),
        FOREIGN KEY(user_id) REFERENCES users(user_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS clan_join_requests(
        request_id INTEGER PRIMARY KEY AUTOINCREMENT,
        clan_id INTEGER NOT NULL,
        user_id INTEGER NOT NULL,
        created_at INTEGER NOT NULL,
        FOREIGN KEY(clan_id) REFERENCES clans(clan_id),
        FOREIGN KEY(user_id) REFERENCES users(user_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS apk_uploads(
        upload_id INTEGER PRIMARY KEY AUTOINCREMENT,
        user_id INTEGER NOT NULL,
        file_id TEXT NOT NULL,
        file_name TEXT,
        file_path TEXT,
        decompiled_path TEXT,
        uploaded_at INTEGER NOT NULL,
        expires_at INTEGER NOT NULL,
        status TEXT DEFAULT 'uploaded'
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS word_list(
        word_id INTEGER PRIMARY KEY AUTOINCREMENT,
        word TEXT UNIQUE NOT NULL,
        language TEXT DEFAULT 'ru'
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS quizzes(
        quiz_id INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT NOT NULL,
        creator_id INTEGER NOT NULL,
        category TEXT DEFAULT 'general',
        difficulty TEXT DEFAULT 'medium',
        reward INTEGER DEFAULT 50,
        xp_reward INTEGER DEFAULT 20,
        created_at INTEGER NOT NULL,
        is_public INTEGER DEFAULT 1,
        times_played INTEGER DEFAULT 0,
        FOREIGN KEY(creator_id) REFERENCES users(user_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS quiz_questions(
        question_id INTEGER PRIMARY KEY AUTOINCREMENT,
        quiz_id INTEGER NOT NULL,
        question_text TEXT NOT NULL,
        option_a TEXT NOT NULL,
        option_b TEXT NOT NULL,
        option_c TEXT,
        option_d TEXT,
        correct_option TEXT NOT NULL,
        time_limit INTEGER DEFAULT 30,
        points INTEGER DEFAULT 10,
        FOREIGN KEY(quiz_id) REFERENCES quizzes(quiz_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS quiz_scores(
        score_id INTEGER PRIMARY KEY AUTOINCREMENT,
        quiz_id INTEGER NOT NULL,
        user_id INTEGER NOT NULL,
        score INTEGER DEFAULT 0,
        correct_answers INTEGER DEFAULT 0,
        total_questions INTEGER DEFAULT 0,
        completed_at INTEGER NOT NULL,
        time_taken INTEGER DEFAULT 0,
        FOREIGN KEY(quiz_id) REFERENCES quizzes(quiz_id),
        FOREIGN KEY(user_id) REFERENCES users(user_id)
    )""")
    
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS code_executions(
        execution_id INTEGER PRIMARY KEY AUTOINCREMENT,
        user_id INTEGER NOT NULL,
        language TEXT NOT NULL,
        code TEXT NOT NULL,
        output TEXT,
        error TEXT,
        execution_time REAL DEFAULT 0,
        executed_at INTEGER NOT NULL,
        FOREIGN KEY(user_id) REFERENCES users(user_id)
    )""")
    
    cursor.execute("PRAGMA table_info(users)")
    columns = [col[1] for col in cursor.fetchall()]
    if 'wins' not in columns:
        cursor.execute("ALTER TABLE users ADD COLUMN wins INTEGER DEFAULT 0")
    if 'losses' not in columns:
        cursor.execute("ALTER TABLE users ADD COLUMN losses INTEGER DEFAULT 0")
    if 'games_played' not in columns:
        cursor.execute("ALTER TABLE users ADD COLUMN games_played INTEGER DEFAULT 0")
    if 'last_daily' not in columns:
        cursor.execute("ALTER TABLE users ADD COLUMN last_daily INTEGER DEFAULT 0")
    
    conn.commit()

    cursor.execute("SELECT COUNT(*) FROM items")
    if cursor.fetchone()[0] == 0:
        seed_items = [
            ("Деревянный меч", "weapon", 3, 30),
            ("Ржавый меч", "weapon", 5, 50),
            ("Железный меч", "weapon", 12, 120),
            ("Стальной меч", "weapon", 20, 250),
            ("Легендарный меч", "weapon", 35, 500),
            ("Тряпичная броня", "armor", 5, 25),
            ("Кожаная броня", "armor", 10, 40),
            ("Железная броня", "armor", 25, 110),
            ("Стальная броня", "armor", 40, 300),
            ("Легендарная броня", "armor", 60, 600),
            ("Малое зелье", "potion", 30, 30),
            ("Среднее зелье", "potion", 60, 60),
            ("Большое зелье", "potion", 100, 100),
            ("Эликсир жизни", "potion", 200, 200),
        ]
        cursor.executemany("INSERT INTO items(name,type,power,price) VALUES (?, ?, ?, ?)", seed_items)
        conn.commit()
    
    cursor.execute("SELECT COUNT(*) FROM word_list")
    if cursor.fetchone()[0] == 0:
        seed_words = [
            "программа", "компьютер", "интернет", "телефон", "игра", "музыка",
            "фильм", "книга", "школа", "университет", "работа", "семья",
            "друзья", "путешествие", "спорт", "футбол", "баскетбол", "теннис",
            "плавание", "бег", "велосипед", "автомобиль", "самолёт", "поезд",
            "корабль", "море", "океан", "гора", "лес", "река", "озеро",
            "город", "деревня", "улица", "дом", "квартира", "комната",
            "кухня", "ванная", "спальня", "гостиная", "окно", "дверь",
            "стол", "стул", "кровать", "диван", "телевизор", "холодильник"
        ]
        cursor.executemany("INSERT INTO word_list(word, language) VALUES (?, 'ru')", [(w,) for w in seed_words])
        conn.commit()

init_db()

class BanMiddleware(BaseMiddleware):
    async def on_pre_process_message(self, message: types.Message, data: dict):
        if is_user_banned(message.from_user.id):
            await message.reply("Ты в бане и не можешь использовать бота.")
            raise CancelHandler()

dp.middleware.setup(BanMiddleware())

def ensure_user(user: types.User):
    username = user.username if user.username else user.first_name
    cursor.execute("SELECT user_id FROM users WHERE user_id = ?", (user.id,))
    if cursor.fetchone() is None:
        cursor.execute(
            "INSERT INTO users(user_id, username, balance, xp, level) VALUES (?, ?, ?, ?, ?)",
            (user.id, username, START_BALANCE, 0, 1)
        )
        conn.commit()
    else:
        cursor.execute("UPDATE users SET username = ? WHERE user_id = ?", (username, user.id))
        conn.commit()

def ensure_user_by_id(user_id, username="unknown"):
    cursor.execute("SELECT user_id FROM users WHERE user_id = ?", (user_id,))
    if cursor.fetchone() is None:
        cursor.execute(
            "INSERT INTO users(user_id, username, balance, xp, level) VALUES (?, ?, ?, ?, ?)",
            (user_id, username, START_BALANCE, 0, 1)
        )
        conn.commit()
    else:
        cursor.execute("UPDATE users SET username = ? WHERE user_id = ?", (username, user_id))
        conn.commit()

def get_user(user_id):
    cursor.execute("SELECT user_id, username, balance, xp, level, wins, losses, games_played, last_daily FROM users WHERE user_id = ?", (user_id,))
    row = cursor.fetchone()
    if row:
        return {
            "user_id": row[0], 
            "username": row[1], 
            "balance": row[2], 
            "xp": row[3], 
            "level": row[4],
            "wins": row[5] or 0,
            "losses": row[6] or 0,
            "games_played": row[7] or 0,
            "last_daily": row[8] or 0
        }
    return None

def update_balance(user_id, delta):
    cursor.execute("UPDATE users SET balance = balance + ? WHERE user_id = ?", (delta, user_id))
    conn.commit()

def add_xp(user_id, xp_gain):
    cursor.execute("SELECT xp, level FROM users WHERE user_id = ?", (user_id,))
    row = cursor.fetchone()
    if not row: return
    xp, lvl = row
    xp += xp_gain
    leveled = 0
    while xp >= LEVEL_XP:
        xp -= LEVEL_XP
        lvl += 1
        leveled += 1
    cursor.execute("UPDATE users SET xp = ?, level = ? WHERE user_id = ?", (xp, lvl, user_id))
    conn.commit()
    return leveled

def remove_xp(user_id, xp_loss):
    cursor.execute("SELECT xp, level FROM users WHERE user_id = ?", (user_id,))
    row = cursor.fetchone()
    if not row:
        return 0
    xp, lvl = row
    xp -= xp_loss
    while xp < 0 and lvl > 1:
        xp += LEVEL_XP
        lvl -= 1
    if xp < 0:
        xp = 0
    cursor.execute("UPDATE users SET xp = ?, level = ? WHERE user_id = ?", (xp, lvl, user_id))
    conn.commit()
    return lvl

def get_items():
    cursor.execute("SELECT item_id, name, type, power, price FROM items")
    return cursor.fetchall()

def get_item(item_id):
    cursor.execute("SELECT item_id, name, type, power, price FROM items WHERE item_id = ?", (item_id,))
    return cursor.fetchone()

def add_item_to_user(user_id, item_id, qty=1):
    cursor.execute("SELECT qty FROM inventory WHERE user_id = ? AND item_id = ?", (user_id, item_id))
    row = cursor.fetchone()
    if row:
        cursor.execute("UPDATE inventory SET qty = qty + ? WHERE user_id = ? AND item_id = ?", (qty, user_id, item_id))
    else:
        cursor.execute("INSERT INTO inventory(user_id, item_id, qty) VALUES (?, ?, ?)", (user_id, item_id, qty))
    conn.commit()

def remove_item_from_user(user_id, item_id, qty=1):
    cursor.execute("SELECT qty FROM inventory WHERE user_id = ? AND item_id = ?", (user_id, item_id))
    row = cursor.fetchone()
    if not row:
        return False
    current_qty = row[0]
    if current_qty <= qty:
        cursor.execute("DELETE FROM inventory WHERE user_id = ? AND item_id = ?", (user_id, item_id))
    else:
        cursor.execute("UPDATE inventory SET qty = qty - ? WHERE user_id = ? AND item_id = ?", (qty, user_id, item_id))
    conn.commit()
    return True

def get_user_inventory(user_id):
    cursor.execute("""
    SELECT i.item_id, it.name, it.type, it.power, it.price, i.qty
    FROM inventory i
    JOIN items it ON i.item_id = it.item_id
    WHERE i.user_id = ?
    """, (user_id,))
    return cursor.fetchall()

def create_loan(user_id, amount):
    created_at = int(time.time())
    cursor.execute("INSERT INTO loans(user_id, amount, created_at) VALUES (?, ?, ?)", (user_id, amount, created_at))
    conn.commit()

def get_user_loans(user_id):
    cursor.execute("SELECT loan_id, amount, created_at FROM loans WHERE user_id = ?", (user_id,))
    return cursor.fetchall()

def delete_loan(loan_id):
    cursor.execute("DELETE FROM loans WHERE loan_id = ?", (loan_id,))
    conn.commit()

def calculate_loan_debt(amount, created_at):
    days_passed = (int(time.time()) - created_at) / 86400
    interest_rate = 0.07
    total_debt = int(amount * ((1 + interest_rate) ** days_passed))
    return total_debt

async def resolve_user_identifier(identifier, bot_instance):
    identifier = identifier.strip()
    if identifier.startswith('@'):
        username = identifier[1:]
        cursor.execute("SELECT user_id, username FROM users WHERE username = ?", (username,))
        row = cursor.fetchone()
        if row:
            return row[0], row[1]
        return None, None
    else:
        try:
            user_id = int(identifier)
            cursor.execute("SELECT user_id, username FROM users WHERE user_id = ?", (user_id,))
            row = cursor.fetchone()
            if row:
                return row[0], row[1]
            try:
                user_info = await bot_instance.get_chat(user_id)
                return user_id, user_info.username or user_info.first_name
            except:
                return user_id, f"ID{user_id}"
        except ValueError:
            return None, None

def is_user_banned(user_id):
    return False

async def is_requester_admin(msg):
    if msg.chat.type != 'private':
        return False
    return msg.from_user.id in ADMINS

def calc_hp(level):
    return 100 + (level - 1) * 10

def attack_range(level):
    base_min = 10 + (level - 1) * 2
    base_max = 20 + (level - 1) * 3
    return base_min, base_max

def get_equipment_effects(user_id):
    inv = get_user_inventory(user_id)
    weapon_power = 0
    armor_percent = 0
    potion_power = 0
    
    for item in inv:
        item_id, name, typ, power, price, qty = item
        if typ == "weapon" and power > weapon_power:
            weapon_power = power
        elif typ == "armor" and power > armor_percent:
            armor_percent = power
        elif typ == "potion" and power > potion_power and qty > 0:
            potion_power = power
    
    return {"weapon_power": weapon_power, "armor_percent": armor_percent, "potion_power": potion_power}

def update_game_stats(user_id, won):
    if won:
        cursor.execute("UPDATE users SET games_played = games_played + 1, wins = wins + 1 WHERE user_id = ?", (user_id,))
    else:
        cursor.execute("UPDATE users SET games_played = games_played + 1, losses = losses + 1 WHERE user_id = ?", (user_id,))
    conn.commit()

async def safe_send_message(chat_id, text=None, thread_id=None, parse_mode=None, **kwargs):
    try:
        if text:
            return await bot.send_message(chat_id, text, message_thread_id=thread_id, parse_mode=parse_mode, **kwargs)
        else:
            return await bot.send_message(chat_id, message_thread_id=thread_id, **kwargs)
    except Exception as e:
        error_msg = str(e)
        if "chat not found" in error_msg.lower():
            raise Exception("Чат не найден. Убедитесь, что ID правильный и бот добавлен в чат.")
        elif "forbidden" in error_msg.lower():
            raise Exception("Бот не имеет прав отправлять сообщения в этот чат.")
        elif "topic" in error_msg.lower() or "thread" in error_msg.lower():
            raise Exception("Тема не найдена или недоступна.")
        else:
            raise Exception(f"Ошибка отправки: {error_msg}")

async def cleanup_expired_apk():
    await asyncio.sleep(10)
    while True:
        try:
            current_time = int(time.time())
            cursor.execute("SELECT upload_id, file_path, decompiled_path FROM apk_uploads WHERE expires_at <= ?", (current_time,))
            expired = cursor.fetchall()
            
            for upload_id, file_path, decompiled_path in expired:
                try:
                    if file_path and os.path.exists(file_path):
                        os.remove(file_path)
                    if decompiled_path and os.path.exists(decompiled_path):
                        shutil.rmtree(decompiled_path, ignore_errors=True)
                    cursor.execute("DELETE FROM apk_uploads WHERE upload_id = ?", (upload_id,))
                    conn.commit()
                except Exception as e:
                    print(f"Cleanup error for upload {upload_id}: {e}")
        except Exception as e:
            print(f"APK cleanup error: {e}")
        
        await asyncio.sleep(3600)

@dp.message_handler(commands=["start"])
async def cmd_start(msg: types.Message):
    ensure_user(msg.from_user)
    welcome_text = f"""
Привет, @{msg.from_user.username}!

**ECSP Guard Bot** - многофункциональный игровой бот

**Основные команды:**
/help - полный список команд
/profile - твой профиль
/shop - магазин предметов
/daily - ежедневный бонус

**Игры:**
/duel @username <ставка> - дуэль
/tictactoe @username - крестики-нолики
/hangman - виселица
/blackjack - блэкджек
/slots <ставка> - слоты
/dice @username <ставка> - битва на костях
/wordchain - цепочка слов
/memory - игра на память
/quiz - викторина

**Программирование (только админ):**
/run_code - выполнить код (админ)
/compile - компилировать код (админ)
/decompile - декомпилировать (админ)
/apk_decompile - разобрать APK (админ)
/apk_compile - собрать APK (админ)

**Викторины:**
/quiz_create - создать викторину
/quiz_list - список викторин
/quiz_play <id> - играть в викторину

**Экономика:**
/balance - баланс
/transfer @username <сумма>
/loan <сумма> - кредит

Удачи!
    """
    await msg.reply(welcome_text)

@dp.message_handler(commands=["help"])
async def cmd_help(msg: types.Message):
    help_text = """
**ПОЛНЫЙ СПИСОК КОМАНД**

**ИГРЫ:**
/duel @username <ставка> - дуэль с игроком
/tictactoe @username - крестики-нолики  
/hangman - виселица (угадай слово)
/blackjack <ставка> - блэкджек (21)
/slots <ставка> - игровые автоматы
/dice @username <ставка> - битва на костях
/wordchain - цепочка слов (групповая)
/memory - игра на память
/guess_number - угадай число

**ВИКТОРИНЫ:**
/quiz_create - создать свою викторину
/quiz_list - список доступных викторин
/quiz_play <id> - начать викторину
/quiz_my - мои созданные викторины
/quiz_delete <id> - удалить викторину
/quiz_top - топ по викторинам

**ПРОГРАММИРОВАНИЕ (только админ):**
/run_code <язык> - выполнить код (админ)
/compile - компилировать код Python (админ)
/decompile - анализ AST кода (админ)
/minify - минифицировать код
/beautify - форматировать код

**APK ИНСТРУМЕНТЫ (только админ):**
/apk_decompile - разобрать APK (админ)
/apk_compile <id> - собрать APK обратно (админ)
/apk_analyze <id> - анализ APK (админ)
/apk_list - список загруженных APK (админ)
/apk_edit <id> <путь> - редактировать файл в APK (админ)

**ЭКОНОМИКА:**
/balance - баланс
/shop - магазин
/buy <id> - купить предмет
/inventory - инвентарь
/transfer @username <сумма> - перевод
/loan <сумма> - взять кредит
/myloans - мои кредиты
/payloan <id> - погасить кредит

**СТАТИСТИКА:**
/stats - твоя статистика
/leaderboard - топ-10 игроков
/profile - профиль
/myid - твой ID

Для дуэлей и игр с ставками нужен достаточный баланс!
    """
    await msg.reply(help_text)

@dp.message_handler(commands=["profile"])
async def cmd_profile(msg: types.Message):
    ensure_user(msg.from_user)
    u = get_user(msg.from_user.id)
    
    wins = u.get('wins', 0)
    losses = u.get('losses', 0)
    games = u.get('games_played', 0)
    winrate = (wins / games * 100) if games > 0 else 0
    
    profile_text = f"""
**Профиль @{u['username']}**

Уровень: {u['level']}
XP: {u['xp']}/{LEVEL_XP}
Баланс: {u['balance']} EcsCoin

**Статистика:**
Побед: {wins}
Поражений: {losses}
Всего игр: {games}
Винрейт: {winrate:.1f}%
    """
    await msg.reply(profile_text)

@dp.message_handler(commands=["balance"])
async def cmd_balance(msg: types.Message):
    ensure_user(msg.from_user)
    u = get_user(msg.from_user.id)
    await msg.reply(f"Твой баланс: {u['balance']} EcsCoin")

@dp.message_handler(commands=["myid"])
async def cmd_myid(msg: types.Message):
    ensure_user(msg.from_user)
    reply_user = msg.reply_to_message.from_user if msg.reply_to_message else msg.from_user
    ensure_user(reply_user)
    await msg.reply(f"Telegram ID @{reply_user.username}: `{reply_user.id}`\n\nИспользуй этот ID для команд типа /duel {reply_user.id} <ставка>")

@dp.message_handler(commands=["shop"])
async def cmd_shop(msg: types.Message):
    ensure_user(msg.from_user)
    items = get_items()
    lines = ["**Магазин предметов:**\n"]
    items_list = []
    
    for item_id, name, typ, power, price in items:
        if price <= 0:
            continue
        if typ == "weapon":
            desc = f"{name} - +{power} урона - {price} монет"
        elif typ == "armor":
            desc = f"{name} - -{power}% к урону - {price} монет"
        else:
            desc = f"{name} - восст. {power} HP - {price} монет"
        items_list.append(f"{item_id}. {desc}")
    
    if items_list:
        lines.append("**Предметы:**")
        lines.extend(items_list)
    
    cursor.execute("SELECT box_id, name, price FROM loot_boxes")
    boxes = cursor.fetchall()
    
    if boxes:
        lines.append("\n**Ящики:**")
        for box_id, name, price in boxes:
            lines.append(f"{box_id}. {name} - {price} монет")
    
    lines.append("\nКупить предмет: /buy <item_id>")
    
    await msg.reply("\n".join(lines))

@dp.message_handler(commands=["buy"])
async def cmd_buy(msg: types.Message):
    ensure_user(msg.from_user)
    parts = msg.text.split()
    if len(parts) < 2:
        await msg.reply("Использование: /buy <item_id>")
        return
    try:
        item_id = int(parts[1])
    except:
        await msg.reply("Неверный id предмета.")
        return
    item = get_item(item_id)
    if not item:
        await msg.reply("Такого предмета нет.")
        return
    _, name, typ, power, price = item
    if price <= 0:
        await msg.reply("Этот предмет нельзя купить в магазине.")
        return
    user = get_user(msg.from_user.id)
    if user['balance'] < price:
        await msg.reply("Недостаточно средств.")
        return
    update_balance(msg.from_user.id, -price)
    add_item_to_user(msg.from_user.id, item_id, 1)
    await msg.reply(f"Ты купил {name} за {price} монет. /inventory")

@dp.message_handler(commands=["inventory"])
async def cmd_inventory(msg: types.Message):
    ensure_user(msg.from_user)
    inv = get_user_inventory(msg.from_user.id)
    if not inv:
        await msg.reply("Инвентарь пуст.")
        return
    lines = ["Твой инвентарь:"]
    for it in inv:
        item_id, name, typ, power, price, qty = it
        lines.append(f"{item_id}. {name} ({typ}) x{qty} - power:{power}")
    await msg.reply("\n".join(lines))

@dp.message_handler(commands=["stats"])
async def cmd_stats(msg: types.Message):
    ensure_user(msg.from_user)
    u = get_user(msg.from_user.id)
    
    wins = u.get('wins', 0)
    losses = u.get('losses', 0)
    games = u.get('games_played', 0)
    winrate = (wins / games * 100) if games > 0 else 0
    
    stats_text = f"""
**Статистика @{u['username']}**

Побед: {wins}
Поражений: {losses}
Всего игр: {games}
Винрейт: {winrate:.1f}%

Уровень: {u['level']}
XP: {u['xp']}/{LEVEL_XP}
Баланс: {u['balance']} EcsCoin
    """
    await msg.reply(stats_text)

@dp.message_handler(commands=["leaderboard"])
async def cmd_leaderboard(msg: types.Message):
    cursor.execute("SELECT username, level, balance, wins, xp FROM users ORDER BY level DESC, xp DESC LIMIT 10")
    top_users = cursor.fetchall()
    
    if not top_users:
        await msg.reply("Таблица лидеров пуста.")
        return
    
    lines = ["**Топ-10 игроков:**\n"]
    medals = ["1.", "2.", "3."]
    
    for idx, (username, level, balance, wins, xp) in enumerate(top_users, 1):
        medal = medals[idx-1] if idx <= 3 else f"{idx}."
        lines.append(f"{medal} @{username} - Ур.{level} | {balance} | {wins} побед")
    
    await msg.reply("\n".join(lines))

@dp.message_handler(commands=["daily"])
async def cmd_daily(msg: types.Message):
    ensure_user(msg.from_user)
    u = get_user(msg.from_user.id)
    
    current_time = int(time.time())
    last_daily = u.get('last_daily', 0)
    
    day_seconds = 86400
    time_diff = current_time - last_daily
    
    if time_diff < day_seconds:
        hours_left = (day_seconds - time_diff) // 3600
        minutes_left = ((day_seconds - time_diff) % 3600) // 60
        await msg.reply(f"Ежедневный бонус уже получен!\nСледующий бонус через: {hours_left}ч {minutes_left}м")
        return
    
    daily_reward = 50 + (u['level'] * 10)
    daily_xp = 20
    
    update_balance(msg.from_user.id, daily_reward)
    add_xp(msg.from_user.id, daily_xp)
    
    cursor.execute("UPDATE users SET last_daily = ? WHERE user_id = ?", (current_time, msg.from_user.id))
    conn.commit()
    
    await msg.reply(f"Ежедневный бонус получен!\n+{daily_reward} EcsCoin\n+{daily_xp} XP")

@dp.message_handler(commands=["transfer"])
async def cmd_transfer(msg: types.Message):
    ensure_user(msg.from_user)
    parts = msg.text.split()
    
    if len(parts) < 3:
        await msg.reply("Использование: /transfer @username|ID <сумма>")
        return
    
    target_identifier = parts[1]
    
    try:
        amount = int(parts[2])
    except:
        await msg.reply("Неверная сумма.")
        return
    
    if amount <= 0:
        await msg.reply("Сумма должна быть положительной!")
        return
    
    target_id, target_name = await resolve_user_identifier(target_identifier, bot)
    if not target_id:
        await msg.reply("Не могу найти пользователя.")
        return
    
    sender = get_user(msg.from_user.id)
    
    if sender['balance'] < amount:
        await msg.reply(f"Недостаточно средств! Твой баланс: {sender['balance']} EcsCoin")
        return
    
    if msg.from_user.id == target_id:
        await msg.reply("Нельзя переводить монеты самому себе!")
        return
    
    ensure_user_by_id(target_id, target_name)
    
    update_balance(msg.from_user.id, -amount)
    update_balance(target_id, amount)
    
    await msg.reply(f"Переведено {amount} EcsCoin пользователю {target_identifier}")
    
    try:
        await bot.send_message(target_id, f"@{msg.from_user.username or msg.from_user.first_name} перевёл тебе {amount} EcsCoin!")
    except:
        pass

@dp.message_handler(commands=["loan"])
async def cmd_loan(msg: types.Message):
    ensure_user(msg.from_user)
    parts = msg.text.split()
    
    if len(parts) < 2:
        await msg.reply("Использование: /loan <сумма>")
        return
    
    try:
        amount = int(parts[1])
    except:
        await msg.reply("Неверная сумма.")
        return
    
    if amount <= 0:
        await msg.reply("Сумма кредита должна быть положительной!")
        return
    
    if amount > 1000:
        await msg.reply("Максимальная сумма кредита - 1000 EcsCoin")
        return
    
    create_loan(msg.from_user.id, amount)
    update_balance(msg.from_user.id, amount)
    
    await msg.reply(f"Кредит на {amount} EcsCoin выдан!\nПроцентная ставка: 7% в сутки\n\nИспользуй /myloans чтобы посмотреть свои кредиты")

@dp.message_handler(commands=["myloans"])
async def cmd_myloans(msg: types.Message):
    ensure_user(msg.from_user)
    loans = get_user_loans(msg.from_user.id)
    
    if not loans:
        await msg.reply("У тебя нет активных кредитов.")
        return
    
    lines = ["**Твои кредиты:**\n"]
    total_debt = 0
    
    for loan_id, amount, created_at in loans:
        current_debt = calculate_loan_debt(amount, created_at)
        days_passed = (int(time.time()) - created_at) / 86400
        total_debt += current_debt
        
        lines.append(f"Кредит #{loan_id}:")
        lines.append(f"  Сумма: {amount} EcsCoin")
        lines.append(f"  Прошло дней: {days_passed:.1f}")
        lines.append(f"  К оплате: {current_debt} EcsCoin")
        lines.append(f"  /payloan {loan_id}\n")
    
    lines.append(f"**Общий долг: {total_debt} EcsCoin**")
    await msg.reply("\n".join(lines))

@dp.message_handler(commands=["payloan"])
async def cmd_payloan(msg: types.Message):
    ensure_user(msg.from_user)
    parts = msg.text.split()
    
    if len(parts) < 2:
        await msg.reply("Использование: /payloan <loan_id>")
        return
    
    try:
        loan_id = int(parts[1])
    except:
        await msg.reply("Неверный ID кредита.")
        return
    
    loans = get_user_loans(msg.from_user.id)
    loan_found = None
    
    for l_id, amount, created_at in loans:
        if l_id == loan_id:
            loan_found = (l_id, amount, created_at)
            break
    
    if not loan_found:
        await msg.reply("Кредит не найден.")
        return
    
    _, amount, created_at = loan_found
    debt = calculate_loan_debt(amount, created_at)
    
    user = get_user(msg.from_user.id)
    if user['balance'] < debt:
        await msg.reply(f"Недостаточно средств!\nНужно: {debt} EcsCoin\nТвой баланс: {user['balance']} EcsCoin")
        return
    
    update_balance(msg.from_user.id, -debt)
    delete_loan(loan_id)
    
    await msg.reply(f"Кредит #{loan_id} погашен!\nСписано: {debt} EcsCoin")


HANGMAN_STAGES = [
    """
   -----
   |   |
       |
       |
       |
       |
  ========
    """,
    """
   -----
   |   |
   O   |
       |
       |
       |
  ========
    """,
    """
   -----
   |   |
   O   |
   |   |
       |
       |
  ========
    """,
    """
   -----
   |   |
   O   |
  /|   |
       |
       |
  ========
    """,
    """
   -----
   |   |
   O   |
  /|\\  |
       |
       |
  ========
    """,
    """
   -----
   |   |
   O   |
  /|\\  |
  /    |
       |
  ========
    """,
    """
   -----
   |   |
   O   |
  /|\\  |
  / \\  |
       |
  ========
    """
]

def get_random_word():
    cursor.execute("SELECT word FROM word_list WHERE language = 'ru' ORDER BY RANDOM() LIMIT 1")
    row = cursor.fetchone()
    return row[0] if row else "программа"

@dp.message_handler(commands=["hangman"])
async def cmd_hangman(msg: types.Message):
    ensure_user(msg.from_user)
    chat_id = msg.chat.id
    user_id = msg.from_user.id
    
    if chat_id in hangman_games:
        await msg.reply("Игра уже идёт! Используй /hangman_stop чтобы остановить")
        return
    
    word = get_random_word().lower()
    hangman_games[chat_id] = {
        'word': word,
        'guessed': set(),
        'attempts': 0,
        'max_attempts': 6,
        'creator': user_id
    }
    
    masked = ' '.join('_' if c not in hangman_games[chat_id]['guessed'] else c for c in word)
    
    await msg.reply(f"""
**ВИСЕЛИЦА**

Слово: `{masked}`
Попытки: {hangman_games[chat_id]['attempts']}/{hangman_games[chat_id]['max_attempts']}

Угадывай по одной букве! Например: а
Или угадай всё слово целиком!

/hangman_stop - остановить игру
    """)

@dp.message_handler(lambda msg: msg.chat.id in hangman_games and len(msg.text.strip()) == 1 and msg.text.strip().isalpha())
async def hangman_guess_letter(msg: types.Message):
    chat_id = msg.chat.id
    letter = msg.text.strip().lower()
    game = hangman_games[chat_id]
    
    if letter in game['guessed']:
        await msg.reply("Эта буква уже была!")
        return
    
    game['guessed'].add(letter)
    
    if letter not in game['word']:
        game['attempts'] += 1
        
        if game['attempts'] >= game['max_attempts']:
            del hangman_games[chat_id]
            update_game_stats(msg.from_user.id, False)
            await msg.reply(f"Проигрыш! Слово было: **{game['word']}**\n\n```{HANGMAN_STAGES[6]}```")
            return
        
        masked = ' '.join('_' if c not in game['guessed'] else c for c in game['word'])
        await msg.reply(f"Нет такой буквы!\n\n```{HANGMAN_STAGES[game['attempts']]}```\n\nСлово: `{masked}`\nПопытки: {game['attempts']}/{game['max_attempts']}")
    else:
        masked = ' '.join('_' if c not in game['guessed'] else c for c in game['word'])
        
        if '_' not in masked:
            reward = 100
            xp = 30
            update_balance(msg.from_user.id, reward)
            add_xp(msg.from_user.id, xp)
            update_game_stats(msg.from_user.id, True)
            del hangman_games[chat_id]
            await msg.reply(f"Победа! Слово: **{game['word']}**\n\n+{reward} EcsCoin\n+{xp} XP")
        else:
            await msg.reply(f"Есть такая буква!\n\n```{HANGMAN_STAGES[game['attempts']]}```\n\nСлово: `{masked}`\nПопытки: {game['attempts']}/{game['max_attempts']}")

@dp.message_handler(lambda msg: msg.chat.id in hangman_games and len(msg.text.strip()) > 1 and msg.text.strip().isalpha())
async def hangman_guess_word(msg: types.Message):
    chat_id = msg.chat.id
    guess = msg.text.strip().lower()
    game = hangman_games[chat_id]
    
    if guess == game['word']:
        reward = 150
        xp = 50
        update_balance(msg.from_user.id, reward)
        add_xp(msg.from_user.id, xp)
        update_game_stats(msg.from_user.id, True)
        del hangman_games[chat_id]
        await msg.reply(f"Угадал всё слово! **{game['word']}**\n\n+{reward} EcsCoin\n+{xp} XP")
    else:
        game['attempts'] += 1
        if game['attempts'] >= game['max_attempts']:
            del hangman_games[chat_id]
            update_game_stats(msg.from_user.id, False)
            await msg.reply(f"Проигрыш! Слово было: **{game['word']}**\n\n```{HANGMAN_STAGES[6]}```")
        else:
            masked = ' '.join('_' if c not in game['guessed'] else c for c in game['word'])
            await msg.reply(f"Неверно!\n\n```{HANGMAN_STAGES[game['attempts']]}```\n\nСлово: `{masked}`\nПопытки: {game['attempts']}/{game['max_attempts']}")

@dp.message_handler(commands=["hangman_stop"])
async def cmd_hangman_stop(msg: types.Message):
    chat_id = msg.chat.id
    if chat_id not in hangman_games:
        await msg.reply("Нет активной игры в виселицу!")
        return
    
    word = hangman_games[chat_id]['word']
    del hangman_games[chat_id]
    await msg.reply(f"Игра остановлена! Слово было: **{word}**")


def create_deck():
    suits = ['S', 'H', 'D', 'C']
    ranks = ['2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K', 'A']
    return [{'rank': r, 'suit': s} for s in suits for r in ranks]

def card_value(card):
    if card['rank'] in ['J', 'Q', 'K']:
        return 10
    elif card['rank'] == 'A':
        return 11
    else:
        return int(card['rank'])

def hand_value(hand):
    value = sum(card_value(c) for c in hand)
    aces = sum(1 for c in hand if c['rank'] == 'A')
    while value > 21 and aces:
        value -= 10
        aces -= 1
    return value

def card_str(card):
    return f"{card['rank']}{card['suit']}"

@dp.message_handler(commands=["blackjack"])
async def cmd_blackjack(msg: types.Message):
    ensure_user(msg.from_user)
    user_id = msg.from_user.id
    
    parts = msg.text.split()
    if len(parts) < 2:
        await msg.reply("Использование: /blackjack <ставка>")
        return
    
    try:
        bet = int(parts[1])
    except:
        await msg.reply("Неверная ставка!")
        return
    
    if bet <= 0:
        await msg.reply("Ставка должна быть положительной!")
        return
    
    user = get_user(user_id)
    if user['balance'] < bet:
        await msg.reply(f"Недостаточно средств! Твой баланс: {user['balance']} EcsCoin")
        return
    
    if user_id in blackjack_games:
        await msg.reply("У тебя уже есть активная игра! Используй кнопки")
        return
    
    update_balance(user_id, -bet)
    
    deck = create_deck()
    random.shuffle(deck)
    
    player_hand = [deck.pop(), deck.pop()]
    dealer_hand = [deck.pop(), deck.pop()]
    
    blackjack_games[user_id] = {
        'deck': deck,
        'player_hand': player_hand,
        'dealer_hand': dealer_hand,
        'bet': bet,
        'chat_id': msg.chat.id
    }
    
    player_val = hand_value(player_hand)
    dealer_visible = card_str(dealer_hand[0])
    
    keyboard = InlineKeyboardMarkup(row_width=2)
    keyboard.add(
        InlineKeyboardButton("Взять карту", callback_data="bj_hit"),
        InlineKeyboardButton("Стоп", callback_data="bj_stand")
    )
    
    if player_val == 21:
        reward = int(bet * 2.5)
        update_balance(user_id, reward)
        add_xp(user_id, 40)
        update_game_stats(user_id, True)
        del blackjack_games[user_id]
        await msg.reply(f"**БЛЭКДЖЕК!**\n\nТвои карты: {' '.join(card_str(c) for c in player_hand)} = {player_val}\n\nПобеда! +{reward} EcsCoin")
    else:
        await msg.reply(
            f"**БЛЭКДЖЕК**\n\n"
            f"Твои карты: {' '.join(card_str(c) for c in player_hand)} = {player_val}\n"
            f"Дилер: {dealer_visible} ?\n\n"
            f"Ставка: {bet} EcsCoin",
            reply_markup=keyboard
        )

@dp.callback_query_handler(lambda c: c.data == "bj_hit")
async def bj_hit(callback: types.CallbackQuery):
    user_id = callback.from_user.id
    
    if user_id not in blackjack_games:
        await callback.answer("Игра не найдена!")
        return
    
    game = blackjack_games[user_id]
    card = game['deck'].pop()
    game['player_hand'].append(card)
    
    player_val = hand_value(game['player_hand'])
    dealer_visible = card_str(game['dealer_hand'][0])
    
    keyboard = InlineKeyboardMarkup(row_width=2)
    keyboard.add(
        InlineKeyboardButton("Взять карту", callback_data="bj_hit"),
        InlineKeyboardButton("Стоп", callback_data="bj_stand")
    )
    
    if player_val > 21:
        update_game_stats(user_id, False)
        del blackjack_games[user_id]
        await callback.message.edit_text(
            f"**БЛЭКДЖЕК**\n\n"
            f"Твои карты: {' '.join(card_str(c) for c in game['player_hand'])} = {player_val}\n"
            f"Дилер: {dealer_visible} ?\n\n"
            f"Перебор! Проигрыш!"
        )
    else:
        await callback.message.edit_text(
            f"**БЛЭКДЖЕК**\n\n"
            f"Твои карты: {' '.join(card_str(c) for c in game['player_hand'])} = {player_val}\n"
            f"Дилер: {dealer_visible} ?\n\n"
            f"Ставка: {game['bet']} EcsCoin",
            reply_markup=keyboard
        )
    
    await callback.answer()

@dp.callback_query_handler(lambda c: c.data == "bj_stand")
async def bj_stand(callback: types.CallbackQuery):
    user_id = callback.from_user.id
    
    if user_id not in blackjack_games:
        await callback.answer("Игра не найдена!")
        return
    
    game = blackjack_games[user_id]
    player_val = hand_value(game['player_hand'])
    
    while hand_value(game['dealer_hand']) < 17:
        game['dealer_hand'].append(game['deck'].pop())
    
    dealer_val = hand_value(game['dealer_hand'])
    
    result_text = ""
    if dealer_val > 21:
        reward = game['bet'] * 2
        update_balance(user_id, reward)
        add_xp(user_id, 30)
        update_game_stats(user_id, True)
        result_text = f"Дилер перебрал! Победа! +{reward} EcsCoin"
    elif player_val > dealer_val:
        reward = game['bet'] * 2
        update_balance(user_id, reward)
        add_xp(user_id, 30)
        update_game_stats(user_id, True)
        result_text = f"Победа! +{reward} EcsCoin"
    elif player_val < dealer_val:
        update_game_stats(user_id, False)
        result_text = f"Проигрыш! -{game['bet']} EcsCoin"
    else:
        update_balance(user_id, game['bet'])
        result_text = f"Ничья! Ставка возвращена"
    
    del blackjack_games[user_id]
    
    await callback.message.edit_text(
        f"**БЛЭКДЖЕК**\n\n"
        f"Твои карты: {' '.join(card_str(c) for c in game['player_hand'])} = {player_val}\n"
        f"Дилер: {' '.join(card_str(c) for c in game['dealer_hand'])} = {dealer_val}\n\n"
        f"{result_text}"
    )
    await callback.answer()


@dp.message_handler(commands=["slots"])
async def cmd_slots(msg: types.Message):
    ensure_user(msg.from_user)
    
    parts = msg.text.split()
    if len(parts) < 2:
        await msg.reply("Использование: /slots <ставка>")
        return
    
    try:
        bet = int(parts[1])
    except:
        await msg.reply("Неверная ставка!")
        return
    
    if bet <= 0:
        await msg.reply("Ставка должна быть положительной!")
        return
    
    user = get_user(msg.from_user.id)
    if user['balance'] < bet:
        await msg.reply(f"Недостаточно средств! Твой баланс: {user['balance']} EcsCoin")
        return
    
    update_balance(msg.from_user.id, -bet)
    
    symbols = ['A', 'B', 'C', 'D', 'E', 'F', '7']
    weights = [30, 25, 20, 15, 5, 3, 2]
    
    reel1 = random.choices(symbols, weights=weights)[0]
    reel2 = random.choices(symbols, weights=weights)[0]
    reel3 = random.choices(symbols, weights=weights)[0]
    
    reward = 0
    multiplier = 0
    
    if reel1 == reel2 == reel3:
        if reel1 == '7':
            multiplier = 100
        elif reel1 == 'F':
            multiplier = 50
        elif reel1 == 'E':
            multiplier = 20
        elif reel1 == 'D':
            multiplier = 10
        elif reel1 == 'C':
            multiplier = 7
        elif reel1 == 'B':
            multiplier = 5
        else:
            multiplier = 3
        
        reward = bet * multiplier
        update_balance(msg.from_user.id, reward)
        add_xp(msg.from_user.id, 25)
        update_game_stats(msg.from_user.id, True)
        await msg.reply(f"**СЛОТЫ**\n\n[ {reel1} | {reel2} | {reel3} ]\n\nДЖЕКПОТ x{multiplier}!\n+{reward} EcsCoin")
    elif reel1 == reel2 or reel2 == reel3 or reel1 == reel3:
        multiplier = 2
        reward = bet * multiplier
        update_balance(msg.from_user.id, reward)
        add_xp(msg.from_user.id, 10)
        await msg.reply(f"**СЛОТЫ**\n\n[ {reel1} | {reel2} | {reel3} ]\n\nПара! x{multiplier}\n+{reward} EcsCoin")
    else:
        update_game_stats(msg.from_user.id, False)
        await msg.reply(f"**СЛОТЫ**\n\n[ {reel1} | {reel2} | {reel3} ]\n\nПроигрыш! -{bet} EcsCoin")


@dp.message_handler(commands=["quiz_create"])
async def cmd_quiz_create(msg: types.Message, state: FSMContext):
    ensure_user(msg.from_user)
    
    await state.update_data(questions=[])
    await QuizCreation.waiting_for_name.set()
    
    await msg.reply(
        "**Создание викторины**\n\n"
        "Введи название викторины:\n\n"
        "/cancel - отменить создание"
    )

@dp.message_handler(state=QuizCreation.waiting_for_name)
async def quiz_name_received(msg: types.Message, state: FSMContext):
    if msg.text.startswith('/cancel'):
        await state.finish()
        await msg.reply("Создание викторины отменено.")
        return
    
    await state.update_data(name=msg.text.strip())
    await QuizCreation.waiting_for_question.set()
    
    await msg.reply(
        f"Название: **{msg.text.strip()}**\n\n"
        "Теперь введи первый вопрос:\n\n"
        "/cancel - отменить"
    )

@dp.message_handler(state=QuizCreation.waiting_for_question)
async def quiz_question_received(msg: types.Message, state: FSMContext):
    if msg.text.startswith('/cancel'):
        await state.finish()
        await msg.reply("Создание викторины отменено.")
        return
    
    await state.update_data(current_question=msg.text.strip())
    await QuizCreation.waiting_for_answers.set()
    
    await msg.reply(
        f"Вопрос: **{msg.text.strip()}**\n\n"
        "Введи варианты ответов (от 2 до 4), каждый на новой строке:\n\n"
        "Пример:\n"
        "Москва\n"
        "Санкт-Петербург\n"
        "Новосибирск\n"
        "Екатеринбург\n\n"
        "/cancel - отменить"
    )

@dp.message_handler(state=QuizCreation.waiting_for_answers)
async def quiz_answers_received(msg: types.Message, state: FSMContext):
    if msg.text.startswith('/cancel'):
        await state.finish()
        await msg.reply("Создание викторины отменено.")
        return
    
    answers = [a.strip() for a in msg.text.split('\n') if a.strip()]
    
    if len(answers) < 2:
        await msg.reply("Нужно минимум 2 варианта ответа! Попробуй ещё раз:")
        return
    
    if len(answers) > 4:
        answers = answers[:4]
    
    await state.update_data(current_answers=answers)
    await QuizCreation.waiting_for_correct.set()
    
    options_text = "\n".join([f"{chr(65+i)}. {a}" for i, a in enumerate(answers)])
    
    await msg.reply(
        f"Варианты ответов:\n{options_text}\n\n"
        "Введи букву правильного ответа (A, B, C или D):\n\n"
        "/cancel - отменить"
    )

@dp.message_handler(state=QuizCreation.waiting_for_correct)
async def quiz_correct_received(msg: types.Message, state: FSMContext):
    if msg.text.startswith('/cancel'):
        await state.finish()
        await msg.reply("Создание викторины отменено.")
        return
    
    correct = msg.text.strip().upper()
    data = await state.get_data()
    answers = data.get('current_answers', [])
    
    valid_options = [chr(65+i) for i in range(len(answers))]
    
    if correct not in valid_options:
        await msg.reply(f"Неверная буква! Выбери из: {', '.join(valid_options)}")
        return
    
    question_data = {
        'question': data['current_question'],
        'answers': answers,
        'correct': correct
    }
    
    questions = data.get('questions', [])
    questions.append(question_data)
    await state.update_data(questions=questions)
    
    await QuizCreation.adding_more.set()
    
    keyboard = InlineKeyboardMarkup(row_width=2)
    keyboard.add(
        InlineKeyboardButton("Добавить вопрос", callback_data="quiz_add_more"),
        InlineKeyboardButton("Завершить", callback_data="quiz_finish")
    )
    
    await msg.reply(
        f"Вопрос #{len(questions)} сохранён!\n\n"
        f"Всего вопросов: {len(questions)}\n\n"
        "Что делаем дальше?",
        reply_markup=keyboard
    )

@dp.callback_query_handler(lambda c: c.data == "quiz_add_more", state=QuizCreation.adding_more)
async def quiz_add_more(callback: types.CallbackQuery, state: FSMContext):
    await QuizCreation.waiting_for_question.set()
    await callback.message.edit_text("Введи следующий вопрос:\n\n/cancel - отменить")
    await callback.answer()

@dp.callback_query_handler(lambda c: c.data == "quiz_finish", state=QuizCreation.adding_more)
async def quiz_finish(callback: types.CallbackQuery, state: FSMContext):
    data = await state.get_data()
    questions = data.get('questions', [])
    name = data.get('name', 'Без названия')
    
    if not questions:
        await callback.message.edit_text("Нужен минимум 1 вопрос!")
        await state.finish()
        return
    
    created_at = int(time.time())
    cursor.execute(
        "INSERT INTO quizzes(name, creator_id, created_at) VALUES (?, ?, ?)",
        (name, callback.from_user.id, created_at)
    )
    quiz_id = cursor.lastrowid
    
    for q in questions:
        answers = q['answers']
        option_a = answers[0] if len(answers) > 0 else ""
        option_b = answers[1] if len(answers) > 1 else ""
        option_c = answers[2] if len(answers) > 2 else None
        option_d = answers[3] if len(answers) > 3 else None
        
        cursor.execute(
            """INSERT INTO quiz_questions(quiz_id, question_text, option_a, option_b, option_c, option_d, correct_option)
               VALUES (?, ?, ?, ?, ?, ?, ?)""",
            (quiz_id, q['question'], option_a, option_b, option_c, option_d, q['correct'])
        )
    
    conn.commit()
    await state.finish()
    
    await callback.message.edit_text(
        f"Викторина **{name}** создана!\n\n"
        f"ID: {quiz_id}\n"
        f"Вопросов: {len(questions)}\n\n"
        f"Чтобы сыграть: /quiz_play {quiz_id}"
    )
    await callback.answer()

@dp.message_handler(commands=["quiz_list"])
async def cmd_quiz_list(msg: types.Message):
    cursor.execute(
        """SELECT q.quiz_id, q.name, u.username, q.times_played,
                  (SELECT COUNT(*) FROM quiz_questions WHERE quiz_id = q.quiz_id) as q_count
           FROM quizzes q
           JOIN users u ON q.creator_id = u.user_id
           WHERE q.is_public = 1
           ORDER BY q.times_played DESC
           LIMIT 20"""
    )
    quizzes = cursor.fetchall()
    
    if not quizzes:
        await msg.reply("Пока нет доступных викторин. Создай первую: /quiz_create")
        return
    
    lines = ["**Доступные викторины:**\n"]
    for quiz_id, name, creator, times_played, q_count in quizzes:
        lines.append(f"{quiz_id}. **{name}** (@{creator})")
        lines.append(f"   Вопросов: {q_count} | Сыграно: {times_played}")
        lines.append(f"   /quiz_play {quiz_id}\n")
    
    await msg.reply("\n".join(lines))

@dp.message_handler(commands=["quiz_play"])
async def cmd_quiz_play(msg: types.Message):
    ensure_user(msg.from_user)
    parts = msg.text.split()
    
    if len(parts) < 2:
        await msg.reply("Использование: /quiz_play <quiz_id>")
        return
    
    try:
        quiz_id = int(parts[1])
    except:
        await msg.reply("Неверный ID викторины.")
        return
    
    cursor.execute("SELECT name, reward, xp_reward FROM quizzes WHERE quiz_id = ?", (quiz_id,))
    quiz = cursor.fetchone()
    
    if not quiz:
        await msg.reply("Викторина не найдена.")
        return
    
    quiz_name, reward, xp_reward = quiz
    
    cursor.execute(
        "SELECT question_id, question_text, option_a, option_b, option_c, option_d, correct_option FROM quiz_questions WHERE quiz_id = ?",
        (quiz_id,)
    )
    questions = cursor.fetchall()
    
    if not questions:
        await msg.reply("В этой викторине нет вопросов.")
        return
    
    user_id = msg.from_user.id
    quiz_sessions[user_id] = {
        'quiz_id': quiz_id,
        'quiz_name': quiz_name,
        'questions': questions,
        'current': 0,
        'score': 0,
        'correct_count': 0,
        'start_time': time.time(),
        'chat_id': msg.chat.id,
        'reward': reward,
        'xp_reward': xp_reward
    }
    
    await send_quiz_question(msg.chat.id, user_id)

async def send_quiz_question(chat_id, user_id):
    session = quiz_sessions.get(user_id)
    if not session:
        return
    
    current = session['current']
    questions = session['questions']
    
    if current >= len(questions):
        await finish_quiz(chat_id, user_id)
        return
    
    q = questions[current]
    q_id, q_text, opt_a, opt_b, opt_c, opt_d, correct = q
    
    keyboard = InlineKeyboardMarkup(row_width=2)
    buttons = [
        InlineKeyboardButton(f"A: {opt_a}", callback_data=f"quiz_ans_A"),
        InlineKeyboardButton(f"B: {opt_b}", callback_data=f"quiz_ans_B"),
    ]
    if opt_c:
        buttons.append(InlineKeyboardButton(f"C: {opt_c}", callback_data=f"quiz_ans_C"))
    if opt_d:
        buttons.append(InlineKeyboardButton(f"D: {opt_d}", callback_data=f"quiz_ans_D"))
    
    keyboard.add(*buttons)
    
    await bot.send_message(
        chat_id,
        f"**{session['quiz_name']}**\n\n"
        f"Вопрос {current + 1}/{len(questions)}:\n\n"
        f"**{q_text}**",
        reply_markup=keyboard
    )

@dp.callback_query_handler(lambda c: c.data.startswith("quiz_ans_"))
async def quiz_answer(callback: types.CallbackQuery):
    user_id = callback.from_user.id
    session = quiz_sessions.get(user_id)
    
    if not session:
        await callback.answer("Сессия не найдена!")
        return
    
    answer = callback.data.split("_")[2]
    current = session['current']
    questions = session['questions']
    
    q = questions[current]
    correct = q[6]
    
    if answer == correct:
        session['score'] += 10
        session['correct_count'] += 1
        await callback.message.edit_text(
            f"{callback.message.text}\n\n"
            f"Правильно! Ответ: {correct}"
        )
    else:
        await callback.message.edit_text(
            f"{callback.message.text}\n\n"
            f"Неправильно! Правильный ответ: {correct}"
        )
    
    session['current'] += 1
    await callback.answer()
    
    await asyncio.sleep(1)
    await send_quiz_question(callback.message.chat.id, user_id)

async def finish_quiz(chat_id, user_id):
    session = quiz_sessions.get(user_id)
    if not session:
        return
    
    time_taken = int(time.time() - session['start_time'])
    correct_count = session['correct_count']
    total = len(session['questions'])
    score = session['score']
    
    percentage = (correct_count / total * 100) if total > 0 else 0
    
    cursor.execute("UPDATE quizzes SET times_played = times_played + 1 WHERE quiz_id = ?", (session['quiz_id'],))
    
    cursor.execute(
        """INSERT INTO quiz_scores(quiz_id, user_id, score, correct_answers, total_questions, completed_at, time_taken)
           VALUES (?, ?, ?, ?, ?, ?, ?)""",
        (session['quiz_id'], user_id, score, correct_count, total, int(time.time()), time_taken)
    )
    conn.commit()
    
    if percentage >= 70:
        reward = session['reward']
        xp = session['xp_reward']
        update_balance(user_id, reward)
        add_xp(user_id, xp)
        reward_text = f"\n\nНаграда: +{reward} EcsCoin, +{xp} XP"
    else:
        reward_text = "\n\nНабери 70%+ правильных ответов для награды!"
    
    del quiz_sessions[user_id]
    
    await bot.send_message(
        chat_id,
        f"**Викторина завершена!**\n\n"
        f"Правильных ответов: {correct_count}/{total} ({percentage:.0f}%)\n"
        f"Очков: {score}\n"
        f"Время: {time_taken} сек{reward_text}"
    )

@dp.message_handler(commands=["quiz_my"])
async def cmd_quiz_my(msg: types.Message):
    ensure_user(msg.from_user)
    
    cursor.execute(
        """SELECT quiz_id, name, times_played,
                  (SELECT COUNT(*) FROM quiz_questions WHERE quiz_id = q.quiz_id) as q_count
           FROM quizzes q
           WHERE creator_id = ?""",
        (msg.from_user.id,)
    )
    quizzes = cursor.fetchall()
    
    if not quizzes:
        await msg.reply("У тебя пока нет созданных викторин. Создай первую: /quiz_create")
        return
    
    lines = ["**Твои викторины:**\n"]
    for quiz_id, name, times_played, q_count in quizzes:
        lines.append(f"{quiz_id}. **{name}**")
        lines.append(f"   Вопросов: {q_count} | Сыграно: {times_played}")
        lines.append(f"   /quiz_delete {quiz_id}\n")
    
    await msg.reply("\n".join(lines))

@dp.message_handler(commands=["quiz_delete"])
async def cmd_quiz_delete(msg: types.Message):
    ensure_user(msg.from_user)
    parts = msg.text.split()
    
    if len(parts) < 2:
        await msg.reply("Использование: /quiz_delete <quiz_id>")
        return
    
    try:
        quiz_id = int(parts[1])
    except:
        await msg.reply("Неверный ID викторины.")
        return
    
    cursor.execute("SELECT creator_id FROM quizzes WHERE quiz_id = ?", (quiz_id,))
    row = cursor.fetchone()
    
    if not row:
        await msg.reply("Викторина не найдена.")
        return
    
    if row[0] != msg.from_user.id and msg.from_user.id not in ADMINS:
        await msg.reply("Ты не можешь удалить эту викторину.")
        return
    
    cursor.execute("DELETE FROM quiz_questions WHERE quiz_id = ?", (quiz_id,))
    cursor.execute("DELETE FROM quiz_scores WHERE quiz_id = ?", (quiz_id,))
    cursor.execute("DELETE FROM quizzes WHERE quiz_id = ?", (quiz_id,))
    conn.commit()
    
    await msg.reply(f"Викторина #{quiz_id} удалена.")

@dp.message_handler(commands=["quiz_top"])
async def cmd_quiz_top(msg: types.Message):
    cursor.execute(
        """SELECT u.username, SUM(qs.score) as total_score, COUNT(*) as games,
                  SUM(qs.correct_answers) as correct, SUM(qs.total_questions) as total
           FROM quiz_scores qs
           JOIN users u ON qs.user_id = u.user_id
           GROUP BY qs.user_id
           ORDER BY total_score DESC
           LIMIT 10"""
    )
    top = cursor.fetchall()
    
    if not top:
        await msg.reply("Пока никто не играл в викторины.")
        return
    
    lines = ["**Топ по викторинам:**\n"]
    for i, (username, score, games, correct, total) in enumerate(top, 1):
        accuracy = (correct / total * 100) if total > 0 else 0
        lines.append(f"{i}. @{username}")
        lines.append(f"   Очков: {score} | Игр: {games} | Точность: {accuracy:.0f}%\n")
    
    await msg.reply("\n".join(lines))


SUPPORTED_LANGUAGES = {
    'python': {'extension': '.py', 'cmd': 'python3'},
    'py': {'extension': '.py', 'cmd': 'python3'},
    'javascript': {'extension': '.js', 'cmd': 'node'},
    'js': {'extension': '.js', 'cmd': 'node'},
    'bash': {'extension': '.sh', 'cmd': 'bash'},
    'sh': {'extension': '.sh', 'cmd': 'bash'},
}

@dp.message_handler(commands=["run_code"])
async def cmd_run_code(msg: types.Message):
    if msg.from_user.id not in ADMINS:
        await msg.reply("Эта команда только для администраторов.")
        return
    ensure_user(msg.from_user)
    parts = msg.text.split(maxsplit=1)
    
    if len(parts) < 2:
        keyboard = InlineKeyboardMarkup(row_width=2)
        keyboard.add(
            InlineKeyboardButton("Python", callback_data="code_lang_python"),
            InlineKeyboardButton("JavaScript", callback_data="code_lang_javascript"),
            InlineKeyboardButton("Bash", callback_data="code_lang_bash"),
        )
        
        await msg.reply(
            "**Выполнение кода**\n\n"
            "Выбери язык программирования:\n\n"
            "Или используй: /run_code <язык>\n"
            "Затем отправь код.\n\n"
            "Поддерживаемые языки: python, javascript, bash",
            reply_markup=keyboard
        )
        return
    
    lang = parts[1].lower()
    if lang not in SUPPORTED_LANGUAGES:
        await msg.reply(f"Язык '{lang}' не поддерживается.\nПоддерживаемые: {', '.join(SUPPORTED_LANGUAGES.keys())}")
        return
    
    code_compile_sessions[msg.from_user.id] = {
        'language': lang,
        'waiting_for_code': True,
        'chat_id': msg.chat.id
    }
    
    await msg.reply(f"Язык: **{lang}**\n\nТеперь отправь код для выполнения:")

@dp.callback_query_handler(lambda c: c.data.startswith("code_lang_"))
async def code_lang_selected(callback: types.CallbackQuery):
    if callback.from_user.id not in ADMINS:
        await callback.answer("Только для администраторов!")
        return
    lang = callback.data.split("_")[2]
    
    code_compile_sessions[callback.from_user.id] = {
        'language': lang,
        'waiting_for_code': True,
        'chat_id': callback.message.chat.id
    }
    
    await callback.message.edit_text(f"Язык: **{lang}**\n\nТеперь отправь код для выполнения:")
    await callback.answer()

@dp.message_handler(lambda msg: msg.from_user.id in code_compile_sessions and code_compile_sessions[msg.from_user.id].get('waiting_for_code'))
async def execute_code(msg: types.Message):
    if msg.from_user.id not in ADMINS:
        if msg.from_user.id in code_compile_sessions:
            del code_compile_sessions[msg.from_user.id]
        return
    session = code_compile_sessions.get(msg.from_user.id)
    if not session:
        return
    
    code = msg.text
    
    if code.startswith('```'):
        code = code.split('\n', 1)[1] if '\n' in code else code[3:]
        if code.endswith('```'):
            code = code[:-3]
    
    lang = session['language']
    lang_info = SUPPORTED_LANGUAGES.get(lang)
    
    if not lang_info:
        await msg.reply("Язык не поддерживается.")
        del code_compile_sessions[msg.from_user.id]
        return
    
    del code_compile_sessions[msg.from_user.id]
    
    await msg.reply("Выполняю код...")
    
    try:
        with tempfile.NamedTemporaryFile(mode='w', suffix=lang_info['extension'], delete=False) as f:
            f.write(code)
            temp_file = f.name
        
        start_time = time.time()
        
        result = subprocess.run(
            [lang_info['cmd'], temp_file],
            capture_output=True,
            text=True,
            timeout=10,
            cwd=tempfile.gettempdir()
        )
        
        execution_time = time.time() - start_time
        
        output = result.stdout[:1500] if result.stdout else ""
        error = result.stderr[:1500] if result.stderr else ""
        
        cursor.execute(
            "INSERT INTO code_executions(user_id, language, code, output, error, execution_time, executed_at) VALUES (?, ?, ?, ?, ?, ?, ?)",
            (msg.from_user.id, lang, code[:2000], output, error, execution_time, int(time.time()))
        )
        conn.commit()
        
        response = f"**Результат выполнения ({lang})**\n\n"
        
        if output:
            response += f"**Вывод:**\n```\n{output}\n```\n"
        
        if error:
            response += f"**Ошибки:**\n```\n{error}\n```\n"
        
        if not output and not error:
            response += "Код выполнен без вывода.\n"
        
        response += f"\nВремя: {execution_time:.3f} сек"
        
        await msg.reply(response)
        
    except subprocess.TimeoutExpired:
        await msg.reply("Время выполнения истекло (лимит 10 секунд).")
    except Exception as e:
        await msg.reply(f"Ошибка выполнения: {str(e)[:500]}")
    finally:
        if 'temp_file' in locals() and os.path.exists(temp_file):
            os.remove(temp_file)

@dp.message_handler(commands=["compile"])
async def cmd_compile(msg: types.Message):
    if msg.from_user.id not in ADMINS:
        await msg.reply("Эта команда только для администраторов.")
        return
    ensure_user(msg.from_user)
    
    if not msg.reply_to_message or not msg.reply_to_message.text:
        await msg.reply(
            "**Компиляция кода**\n\n"
            "Ответь на сообщение с кодом командой /compile\n\n"
            "Поддерживается: Python (bytecode)"
        )
        return
    
    code = msg.reply_to_message.text
    
    if code.startswith('```'):
        lines = code.split('\n')
        code = '\n'.join(lines[1:])
        if code.endswith('```'):
            code = code[:-3]
    
    try:
        compiled = compile(code, '<string>', 'exec')
        
        bytecode_info = []
        bytecode_info.append(f"Константы: {compiled.co_consts}")
        bytecode_info.append(f"Имена: {compiled.co_names}")
        bytecode_info.append(f"Количество переменных: {compiled.co_nlocals}")
        bytecode_info.append(f"Размер стека: {compiled.co_stacksize}")
        
        import dis
        bytecode_str = io.StringIO()
        dis.dis(compiled, file=bytecode_str)
        bytecode = bytecode_str.getvalue()[:1500]
        
        result = "**Компиляция Python**\n\n"
        result += "**Информация:**\n"
        result += '\n'.join(bytecode_info) + "\n\n"
        result += f"**Байткод:**\n```\n{bytecode}\n```"
        
        await msg.reply(result)
        
    except SyntaxError as e:
        await msg.reply(f"Ошибка синтаксиса:\n\n{e}")
    except Exception as e:
        await msg.reply(f"Ошибка компиляции: {e}")

@dp.message_handler(commands=["decompile"])
async def cmd_decompile(msg: types.Message):
    if msg.from_user.id not in ADMINS:
        await msg.reply("Эта команда только для администраторов.")
        return
    ensure_user(msg.from_user)
    
    if not msg.reply_to_message or not msg.reply_to_message.text:
        await msg.reply(
            "**Декомпиляция/Анализ кода**\n\n"
            "Ответь на сообщение с кодом командой /decompile\n\n"
            "Показывает AST (абстрактное синтаксическое дерево)"
        )
        return
    
    code = msg.reply_to_message.text
    
    if code.startswith('```'):
        lines = code.split('\n')
        code = '\n'.join(lines[1:])
        if code.endswith('```'):
            code = code[:-3]
    
    try:
        import ast
        tree = ast.parse(code)
        
        ast_dump = ast.dump(tree, indent=2)[:2000]
        
        imports = []
        functions = []
        classes = []
        
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                for alias in node.names:
                    imports.append(alias.name)
            elif isinstance(node, ast.ImportFrom):
                imports.append(f"from {node.module}")
            elif isinstance(node, ast.FunctionDef):
                functions.append(node.name)
            elif isinstance(node, ast.ClassDef):
                classes.append(node.name)
        
        result = "**Анализ кода (AST)**\n\n"
        
        if imports:
            result += f"**Импорты:** {', '.join(imports[:10])}\n"
        if functions:
            result += f"**Функции:** {', '.join(functions[:10])}\n"
        if classes:
            result += f"**Классы:** {', '.join(classes[:10])}\n"
        
        result += f"\n**AST:**\n```\n{ast_dump}\n```"
        
        await msg.reply(result)
        
    except SyntaxError as e:
        await msg.reply(f"Ошибка синтаксиса:\n\n{e}")
    except Exception as e:
        await msg.reply(f"Ошибка анализа: {e}")

@dp.message_handler(commands=["minify"])
async def cmd_minify(msg: types.Message):
    ensure_user(msg.from_user)
    
    if not msg.reply_to_message or not msg.reply_to_message.text:
        await msg.reply("Ответь на сообщение с кодом командой /minify")
        return
    
    code = msg.reply_to_message.text
    
    if code.startswith('```'):
        lines = code.split('\n')
        lang = lines[0][3:].strip().lower() if lines[0][3:] else 'python'
        code = '\n'.join(lines[1:])
        if code.endswith('```'):
            code = code[:-3]
    else:
        lang = 'python'
    
    try:
        if lang in ['js', 'javascript']:
            try:
                import jsbeautifier
                opts = jsbeautifier.default_options()
                minified = code.replace('\n', ' ').replace('  ', ' ')
            except ImportError:
                minified = code.replace('\n', ' ').replace('  ', ' ')
        else:
            try:
                import python_minifier
                minified = python_minifier.minify(code, remove_literal_statements=True)
            except ImportError:
                lines = code.split('\n')
                minified = '\n'.join(line for line in lines if line.strip() and not line.strip().startswith('#'))
        
        original_size = len(code)
        minified_size = len(minified)
        savings = ((original_size - minified_size) / original_size * 100) if original_size > 0 else 0
        
        result = f"**Минификация ({lang})**\n\n"
        result += f"Оригинал: {original_size} символов\n"
        result += f"После: {minified_size} символов\n"
        result += f"Экономия: {savings:.1f}%\n\n"
        result += f"```\n{minified[:1500]}\n```"
        
        await msg.reply(result)
        
    except Exception as e:
        await msg.reply(f"Ошибка минификации: {e}")

@dp.message_handler(commands=["beautify"])
async def cmd_beautify(msg: types.Message):
    ensure_user(msg.from_user)
    
    if not msg.reply_to_message or not msg.reply_to_message.text:
        await msg.reply("Ответь на сообщение с кодом командой /beautify")
        return
    
    code = msg.reply_to_message.text
    
    if code.startswith('```'):
        lines = code.split('\n')
        lang = lines[0][3:].strip().lower() if lines[0][3:] else 'python'
        code = '\n'.join(lines[1:])
        if code.endswith('```'):
            code = code[:-3]
    else:
        lang = 'python'
    
    try:
        if lang in ['js', 'javascript']:
            try:
                import jsbeautifier
                beautified = jsbeautifier.beautify(code)
            except ImportError:
                beautified = code
        elif lang == 'css':
            try:
                import cssbeautifier
                beautified = cssbeautifier.beautify(code)
            except ImportError:
                beautified = code
        else:
            import textwrap
            beautified = code
        
        result = f"**Форматирование ({lang})**\n\n```{lang}\n{beautified[:1500]}\n```"
        
        await msg.reply(result)
        
    except Exception as e:
        await msg.reply(f"Ошибка форматирования: {e}")


APK_STORAGE = "apk_storage"
os.makedirs(APK_STORAGE, exist_ok=True)

@dp.message_handler(commands=["apk_decompile"])
async def cmd_apk_decompile(msg: types.Message):
    if not await is_requester_admin(msg) and msg.from_user.id not in ADMINS:
        await msg.reply("Эта команда только для администраторов.")
        return
    
    if not msg.reply_to_message or not msg.reply_to_message.document:
        await msg.reply(
            "**Декомпиляция APK**\n\n"
            "Ответь на сообщение с APK файлом командой /apk_decompile\n\n"
            "Бот разберёт APK и покажет структуру."
        )
        return
    
    doc = msg.reply_to_message.document
    
    if not doc.file_name.endswith('.apk'):
        await msg.reply("Файл должен быть в формате .apk")
        return
    
    await msg.reply("Скачиваю и анализирую APK...")
    
    try:
        file_info = await bot.get_file(doc.file_id)
        file_path = os.path.join(APK_STORAGE, f"{msg.from_user.id}_{int(time.time())}_{doc.file_name}")
        
        await bot.download_file(file_info.file_path, file_path)
        
        decompiled_path = file_path.replace('.apk', '_decompiled')
        os.makedirs(decompiled_path, exist_ok=True)
        
        with zipfile.ZipFile(file_path, 'r') as zip_ref:
            zip_ref.extractall(decompiled_path)
        
        expires_at = int(time.time()) + 86400
        
        cursor.execute(
            """INSERT INTO apk_uploads(user_id, file_id, file_name, file_path, decompiled_path, uploaded_at, expires_at, status)
               VALUES (?, ?, ?, ?, ?, ?, ?, 'decompiled')""",
            (msg.from_user.id, doc.file_id, doc.file_name, file_path, decompiled_path, int(time.time()), expires_at)
        )
        upload_id = cursor.lastrowid
        conn.commit()
        
        files = []
        folders = []
        for root, dirs, filenames in os.walk(decompiled_path):
            rel_root = os.path.relpath(root, decompiled_path)
            for d in dirs[:10]:
                folders.append(os.path.join(rel_root, d) if rel_root != '.' else d)
            for f in filenames[:20]:
                files.append(os.path.join(rel_root, f) if rel_root != '.' else f)
        
        manifest_path = os.path.join(decompiled_path, 'AndroidManifest.xml')
        manifest_info = ""
        if os.path.exists(manifest_path):
            try:
                with open(manifest_path, 'rb') as f:
                    content = f.read()
                    if b'package=' in content:
                        manifest_info = "AndroidManifest.xml найден (бинарный формат)"
                    else:
                        manifest_info = "AndroidManifest.xml найден"
            except:
                manifest_info = "AndroidManifest.xml найден"
        
        result = f"**APK Декомпилирован!**\n\n"
        result += f"ID: {upload_id}\n"
        result += f"Файл: {doc.file_name}\n"
        result += f"Истекает через: 24 часа\n\n"
        
        if manifest_info:
            result += f"{manifest_info}\n\n"
        
        result += f"**Папки ({len(folders)}):**\n"
        result += '\n'.join(folders[:10]) + "\n\n"
        
        result += f"**Файлы ({len(files)}):**\n"
        result += '\n'.join(files[:15]) + "\n\n"
        
        result += f"Команды:\n"
        result += f"/apk_analyze {upload_id} - подробный анализ\n"
        result += f"/apk_view {upload_id} <путь> - просмотр файла\n"
        result += f"/apk_compile {upload_id} - собрать обратно"
        
        await msg.reply(result)
        
    except Exception as e:
        await msg.reply(f"Ошибка декомпиляции: {str(e)[:500]}")

@dp.message_handler(commands=["apk_analyze"])
async def cmd_apk_analyze(msg: types.Message):
    if not await is_requester_admin(msg) and msg.from_user.id not in ADMINS:
        await msg.reply("Эта команда только для администраторов.")
        return
    
    parts = msg.text.split()
    if len(parts) < 2:
        await msg.reply("Использование: /apk_analyze <upload_id>")
        return
    
    try:
        upload_id = int(parts[1])
    except:
        await msg.reply("Неверный ID.")
        return
    
    cursor.execute("SELECT file_name, decompiled_path FROM apk_uploads WHERE upload_id = ?", (upload_id,))
    row = cursor.fetchone()
    
    if not row:
        await msg.reply("APK не найден.")
        return
    
    file_name, decompiled_path = row
    
    if not os.path.exists(decompiled_path):
        await msg.reply("Декомпилированные файлы не найдены.")
        return
    
    analysis = {
        'dex_files': [],
        'resources': [],
        'assets': [],
        'libs': [],
        'total_files': 0,
        'total_size': 0
    }
    
    for root, dirs, files in os.walk(decompiled_path):
        for f in files:
            full_path = os.path.join(root, f)
            rel_path = os.path.relpath(full_path, decompiled_path)
            file_size = os.path.getsize(full_path)
            
            analysis['total_files'] += 1
            analysis['total_size'] += file_size
            
            if f.endswith('.dex'):
                analysis['dex_files'].append(rel_path)
            elif rel_path.startswith('res/'):
                analysis['resources'].append(rel_path)
            elif rel_path.startswith('assets/'):
                analysis['assets'].append(rel_path)
            elif rel_path.startswith('lib/'):
                analysis['libs'].append(rel_path)
    
    result = f"**Анализ APK: {file_name}**\n\n"
    result += f"Всего файлов: {analysis['total_files']}\n"
    result += f"Общий размер: {analysis['total_size'] / 1024 / 1024:.2f} MB\n\n"
    
    if analysis['dex_files']:
        result += f"**DEX файлы ({len(analysis['dex_files'])}):**\n"
        result += '\n'.join(analysis['dex_files'][:5]) + "\n\n"
    
    if analysis['libs']:
        result += f"**Нативные библиотеки ({len(analysis['libs'])}):**\n"
        result += '\n'.join(analysis['libs'][:10]) + "\n\n"
    
    result += f"Ресурсов: {len(analysis['resources'])}\n"
    result += f"Assets: {len(analysis['assets'])}"
    
    await msg.reply(result)

@dp.message_handler(commands=["apk_view"])
async def cmd_apk_view(msg: types.Message):
    if not await is_requester_admin(msg) and msg.from_user.id not in ADMINS:
        await msg.reply("Эта команда только для администраторов.")
        return
    
    parts = msg.text.split(maxsplit=2)
    if len(parts) < 3:
        await msg.reply("Использование: /apk_view <upload_id> <путь к файлу>")
        return
    
    try:
        upload_id = int(parts[1])
    except:
        await msg.reply("Неверный ID.")
        return
    
    file_to_view = parts[2]
    
    cursor.execute("SELECT decompiled_path FROM apk_uploads WHERE upload_id = ?", (upload_id,))
    row = cursor.fetchone()
    
    if not row:
        await msg.reply("APK не найден.")
        return
    
    decompiled_path = row[0]
    target_file = os.path.join(decompiled_path, file_to_view)
    
    if not os.path.exists(target_file):
        await msg.reply("Файл не найден.")
        return
    
    if not os.path.isfile(target_file):
        await msg.reply("Это папка, не файл.")
        return
    
    try:
        file_size = os.path.getsize(target_file)
        
        if file_size > 50000:
            await msg.reply(f"Файл слишком большой ({file_size} байт). Максимум 50KB.")
            return
        
        with open(target_file, 'rb') as f:
            content = f.read()
        
        try:
            text_content = content.decode('utf-8')
            await msg.reply(f"**{file_to_view}**\n\n```\n{text_content[:3000]}\n```")
        except:
            hex_content = content[:500].hex()
            await msg.reply(f"**{file_to_view}** (бинарный)\n\n```\n{hex_content}\n```")
            
    except Exception as e:
        await msg.reply(f"Ошибка чтения: {e}")

@dp.message_handler(commands=["apk_compile"])
async def cmd_apk_compile(msg: types.Message):
    if not await is_requester_admin(msg) and msg.from_user.id not in ADMINS:
        await msg.reply("Эта команда только для администраторов.")
        return
    
    parts = msg.text.split()
    if len(parts) < 2:
        await msg.reply("Использование: /apk_compile <upload_id>")
        return
    
    try:
        upload_id = int(parts[1])
    except:
        await msg.reply("Неверный ID.")
        return
    
    cursor.execute("SELECT file_name, decompiled_path FROM apk_uploads WHERE upload_id = ?", (upload_id,))
    row = cursor.fetchone()
    
    if not row:
        await msg.reply("APK не найден.")
        return
    
    file_name, decompiled_path = row
    
    if not os.path.exists(decompiled_path):
        await msg.reply("Декомпилированные файлы не найдены.")
        return
    
    await msg.reply("Собираю APK...")
    
    try:
        output_path = decompiled_path.replace('_decompiled', '_rebuilt.apk')
        
        with zipfile.ZipFile(output_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
            for root, dirs, files in os.walk(decompiled_path):
                for file in files:
                    file_path = os.path.join(root, file)
                    arcname = os.path.relpath(file_path, decompiled_path)
                    zipf.write(file_path, arcname)
        
        file_size = os.path.getsize(output_path) / 1024 / 1024
        
        with open(output_path, 'rb') as f:
            await bot.send_document(
                msg.chat.id,
                types.InputFile(f, filename=file_name.replace('.apk', '_rebuilt.apk')),
                caption=f"Собранный APK\nРазмер: {file_size:.2f} MB\n\nЗамечание: APK не подписан!"
            )
        
        os.remove(output_path)
        
    except Exception as e:
        await msg.reply(f"Ошибка сборки: {str(e)[:500]}")

@dp.message_handler(commands=["apk_list"])
async def cmd_apk_list(msg: types.Message):
    if not await is_requester_admin(msg) and msg.from_user.id not in ADMINS:
        await msg.reply("Эта команда только для администраторов.")
        return
    
    cursor.execute(
        """SELECT upload_id, file_name, uploaded_at, expires_at, status
           FROM apk_uploads
           WHERE user_id = ?
           ORDER BY uploaded_at DESC
           LIMIT 10""",
        (msg.from_user.id,)
    )
    uploads = cursor.fetchall()
    
    if not uploads:
        await msg.reply("У тебя нет загруженных APK.")
        return
    
    lines = ["**Твои APK:**\n"]
    current_time = int(time.time())
    
    for upload_id, file_name, uploaded_at, expires_at, status in uploads:
        time_left = expires_at - current_time
        if time_left < 0:
            status_text = "истёк"
        else:
            hours = time_left // 3600
            status_text = f"осталось {hours}ч"
        
        lines.append(f"{upload_id}. {file_name}")
        lines.append(f"   Статус: {status} | {status_text}\n")
    
    await msg.reply("\n".join(lines))

@dp.message_handler(commands=["apk_delete"])
async def cmd_apk_delete(msg: types.Message):
    if not await is_requester_admin(msg) and msg.from_user.id not in ADMINS:
        await msg.reply("Эта команда только для администраторов.")
        return
    
    parts = msg.text.split()
    if len(parts) < 2:
        await msg.reply("Использование: /apk_delete <upload_id>")
        return
    
    try:
        upload_id = int(parts[1])
    except:
        await msg.reply("Неверный ID.")
        return
    
    cursor.execute("SELECT file_path, decompiled_path FROM apk_uploads WHERE upload_id = ? AND user_id = ?", 
                   (upload_id, msg.from_user.id))
    row = cursor.fetchone()
    
    if not row:
        await msg.reply("APK не найден.")
        return
    
    file_path, decompiled_path = row
    
    try:
        if file_path and os.path.exists(file_path):
            os.remove(file_path)
        if decompiled_path and os.path.exists(decompiled_path):
            shutil.rmtree(decompiled_path, ignore_errors=True)
        
        cursor.execute("DELETE FROM apk_uploads WHERE upload_id = ?", (upload_id,))
        conn.commit()
        
        await msg.reply(f"APK #{upload_id} удалён.")
    except Exception as e:
        await msg.reply(f"Ошибка удаления: {e}")


@dp.message_handler(commands=["cancel"], state="*")
async def cmd_cancel(msg: types.Message, state: FSMContext):
    current_state = await state.get_state()
    if current_state is None:
        await msg.reply("Нечего отменять.")
        return
    
    await state.finish()
    await msg.reply("Действие отменено.")


if __name__ == "__main__":
    print("ECSP Guard Bot starting...")
    print(f"Admins: {ADMINS}")
    loop = asyncio.get_event_loop()
    loop.create_task(cleanup_expired_apk())
    executor.start_polling(dp, skip_updates=True)
