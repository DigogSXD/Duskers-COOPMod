import socket
import threading
import json
import sys

def receive_loop(sock):
    buffer = ""
    while True:
        try:
            data = sock.recv(4096)
            if not data:
                print("\n\033[93m[COOP] Conexão encerrada pelo Host.\033[0m")
                break
            buffer += data.decode("utf-8", errors="ignore")
            while "\n" in buffer:
                line, buffer = buffer.split("\n", 1)
                line = line.strip()
                if not line:
                    continue
                try:
                    packet = json.loads(line)
                    ptype = packet.get("type", "")
                    raw_data = packet.get("data", "")
                    
                    if ptype == "CONSOLE_TEXT":
                        cdata = json.loads(raw_data) if isinstance(raw_data, str) else raw_data
                        text = cdata.get("text", "")
                        print(f"\033[92m{text}\033[0m")
                    elif ptype == "SYSTEM_LOG":
                        print(f"\033[96m{raw_data}\033[0m")
                    elif ptype == "HANDSHAKE":
                        hdata = json.loads(raw_data) if isinstance(raw_data, str) else raw_data
                        pname = hdata.get("playerName", "Operator")
                        print(f"\033[93m[HANDSHAKE] Conectado com sucesso como {pname}!\033[0m")
                    else:
                        print(f"[{ptype}] {raw_data}")
                except Exception as ex:
                    print(f"[RAW] {line}")
        except Exception as e:
            print(f"\n[COOP] Desconectado: {e}")
            break

def main():
    host = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1"
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 7788

    # Support passing DSK-XXXX codes directly in CLI!
    if host.upper().startswith("DSK-"):
        clean = host.upper().replace("DSK-", "").replace("-", "")
        if len(clean) == 12:
            try:
                b = bytes.fromhex(clean)
                host = f"{b[0]}.{b[1]}.{b[2]}.{b[3]}"
                port = int.from_bytes(b[4:6], byteorder='little')
            except Exception:
                pass

    print("=" * 60)
    print(" DUSKERS MULTIPLAYER REMOTE TERMINAL (COOPERATIVO)")
    print("=" * 60)
    print(f"Conectando a {host}:{port}...")

    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.connect((host, port))
        print("\033[92m[COOP] Conectado à ponte de comando do Duskers!\033[0m")
        print("Digite comandos (ex: 1 a1, 2 r3, open d1, generator) ou 'exit' para sair.\n")
    except Exception as e:
        print(f"\033[91m[ERRO] Não foi possível conectar a {host}:{port}: {e}\033[0m")
        print("Verifique se o Host já clicou em '[H]ost Session' no menu do Duskers.")
        return

    t = threading.Thread(target=receive_loop, args=(sock,), daemon=True)
    t.start()

    while True:
        try:
            cmd = input()
            if not cmd:
                continue
            if cmd.lower() in ["exit", "quit"]:
                break

            packet = {
                "type": "COMMAND",
                "sender": "Operator",
                "data": json.dumps({"command": cmd})
            }
            payload = (json.dumps(packet) + "\n").encode("utf-8")
            sock.sendall(payload)
        except (KeyboardInterrupt, EOFError):
            break

    sock.close()
    print("[COOP] Sessão encerrada.")

if __name__ == "__main__":
    main()
