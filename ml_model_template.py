import os
import math
import random
import numpy as np
import pandas as pd
import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.data import Dataset, DataLoader
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler
from sklearn.metrics import mean_absolute_error, mean_squared_error, r2_score

torch.manual_seed(42)
np.random.seed(42)
random.seed(42)

DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")
CSV_PATH = "tornado_ml_data.csv"
BATCH_SIZE = 128
EPOCHS = 80
LEARNING_RATE = 1e-3
WEIGHT_DECAY = 1e-5

INPUT_COLUMNS = [
    "latitude",
    "longitude",
    "elevation",
    "temperature",
    "dewpoint",
    "humidity",
    "pressure",
    "wind_u",
    "wind_v",
    "wind_speed_surface",
    "wind_speed_mid",
    "wind_direction",
    "cape",
    "cin",
    "srh",
    "lifted_index",
    "shear_0_1km",
    "shear_0_6km",
    "precipitable_water",
    "lcl_height",
    "terrain_roughness",
    "land_cover_index",
    "storm_motion",
    "vorticity_index",
    "updraft_index"
]

TARGET_COLUMNS = [
    "pred_wind_speed",
    "pred_vortex_radius",
    "pred_debris_density",
    "pred_pressure_drop",
    "pred_angular_velocity"
]

def make_synthetic_dataframe(n=6000):
    lat = np.random.uniform(25, 49, n)
    lon = np.random.uniform(-105, -80, n)
    elevation = np.random.uniform(0, 2200, n)
    temperature = np.random.uniform(15, 38, n)
    dewpoint = np.random.uniform(5, 28, n)
    humidity = np.random.uniform(25, 100, n)
    pressure = np.random.uniform(960, 1025, n)
    wind_u = np.random.normal(0, 15, n)
    wind_v = np.random.normal(0, 15, n)
    wind_speed_surface = np.sqrt(wind_u**2 + wind_v**2) + np.random.normal(0, 1, n)
    wind_speed_mid = wind_speed_surface + np.random.uniform(3, 25, n)
    wind_direction = np.random.uniform(0, 360, n)
    cape = np.random.uniform(0, 5000, n)
    cin = np.random.uniform(0, 350, n)
    srh = np.random.uniform(0, 600, n)
    lifted_index = np.random.uniform(-12, 6, n)
    shear_0_1km = np.random.uniform(0, 50, n)
    shear_0_6km = np.random.uniform(5, 80, n)
    precipitable_water = np.random.uniform(0.5, 2.8, n)
    lcl_height = np.random.uniform(200, 2500, n)
    terrain_roughness = np.random.uniform(0, 1, n)
    land_cover_index = np.random.uniform(0, 1, n)
    storm_motion = np.random.uniform(5, 80, n)
    vorticity_index = np.random.uniform(0, 1, n)
    updraft_index = np.random.uniform(0, 1, n)

    base_energy = 0.012 * cape + 0.21 * srh + 0.7 * shear_0_6km - 0.5 * cin - 0.8 * lifted_index
    moisture_factor = 0.8 * dewpoint + 0.16 * humidity + 12 * precipitable_water
    wind_factor = 1.5 * wind_speed_surface + 2.1 * wind_speed_mid + 0.9 * shear_0_1km
    terrain_factor = 8 * terrain_roughness + 5 * land_cover_index - 0.002 * elevation
    dynamic_term = 50 * vorticity_index + 60 * updraft_index + 0.35 * storm_motion

    pred_wind_speed = 20 + 0.08 * base_energy + 0.12 * moisture_factor + 0.25 * wind_factor + dynamic_term + np.random.normal(0, 12, n)
    pred_vortex_radius = 30 + 0.015 * cape + 0.3 * wind_speed_mid + 0.18 * humidity + 12 * terrain_roughness + np.random.normal(0, 8, n)
    pred_debris_density = 10 + 12 * terrain_roughness + 10 * land_cover_index + 0.03 * pred_wind_speed + np.random.normal(0, 4, n)
    pred_pressure_drop = 5 + 0.02 * pred_wind_speed + 0.003 * cape + 0.05 * srh + np.random.normal(0, 3, n)
    pred_angular_velocity = 0.2 + 0.002 * pred_wind_speed + 0.0008 * srh + 0.4 * vorticity_index + np.random.normal(0, 0.08, n)

    df = pd.DataFrame({
        "latitude": lat,
        "longitude": lon,
        "elevation": elevation,
        "temperature": temperature,
        "dewpoint": dewpoint,
        "humidity": humidity,
        "pressure": pressure,
        "wind_u": wind_u,
        "wind_v": wind_v,
        "wind_speed_surface": wind_speed_surface,
        "wind_speed_mid": wind_speed_mid,
        "wind_direction": wind_direction,
        "cape": cape,
        "cin": cin,
        "srh": srh,
        "lifted_index": lifted_index,
        "shear_0_1km": shear_0_1km,
        "shear_0_6km": shear_0_6km,
        "precipitable_water": precipitable_water,
        "lcl_height": lcl_height,
        "terrain_roughness": terrain_roughness,
        "land_cover_index": land_cover_index,
        "storm_motion": storm_motion,
        "vorticity_index": vorticity_index,
        "updraft_index": updraft_index,
        "pred_wind_speed": pred_wind_speed,
        "pred_vortex_radius": pred_vortex_radius,
        "pred_debris_density": pred_debris_density,
        "pred_pressure_drop": pred_pressure_drop,
        "pred_angular_velocity": pred_angular_velocity
    })
    return df

def feature_engineering(df):
    df = df.copy()
    df["wind_dir_sin"] = np.sin(np.deg2rad(df["wind_direction"]))
    df["wind_dir_cos"] = np.cos(np.deg2rad(df["wind_direction"]))
    df["wind_vector_mag"] = np.sqrt(df["wind_u"]**2 + df["wind_v"]**2)
    df["temp_dew_spread"] = df["temperature"] - df["dewpoint"]
    df["instability_ratio"] = df["cape"] / (df["cin"] + 1.0)
    df["shear_ratio"] = df["shear_0_6km"] / (df["shear_0_1km"] + 1.0)
    df["moisture_energy"] = df["humidity"] * df["precipitable_water"]
    df["terrain_wind_interaction"] = df["terrain_roughness"] * df["wind_speed_surface"]
    df["storm_spin_proxy"] = df["srh"] * df["vorticity_index"]
    df["updraft_shear_proxy"] = df["updraft_index"] * df["shear_0_6km"]
    df["geo_lat_lon"] = df["latitude"] * np.abs(df["longitude"])
    df["pressure_deficit"] = 1025 - df["pressure"]
    return df

class TornadoDataset(Dataset):
    def __init__(self, x, y):
        self.x = torch.tensor(x, dtype=torch.float32)
        self.y = torch.tensor(y, dtype=torch.float32)

    def __len__(self):
        return len(self.x)

    def __getitem__(self, idx):
        return self.x[idx], self.y[idx]

class GatedFeatureBlock(nn.Module):
    def __init__(self, dim, expansion=2, dropout=0.1):
        super().__init__()
        hidden = dim * expansion
        self.norm = nn.LayerNorm(dim)
        self.fc1 = nn.Linear(dim, hidden)
        self.fc2 = nn.Linear(hidden, dim)
        self.gate = nn.Linear(dim, dim)
        self.dropout = nn.Dropout(dropout)

    def forward(self, x):
        residual = x
        x = self.norm(x)
        gate = torch.sigmoid(self.gate(x))
        x = self.fc1(x)
        x = F.gelu(x)
        x = self.dropout(x)
        x = self.fc2(x)
        x = x * gate
        x = self.dropout(x)
        return x + residual

class CrossFeatureMixer(nn.Module):
    def __init__(self, dim, heads=4, dropout=0.1):
        super().__init__()
        self.norm = nn.LayerNorm(dim)
        self.q = nn.Linear(dim, dim)
        self.k = nn.Linear(dim, dim)
        self.v = nn.Linear(dim, dim)
        self.proj = nn.Linear(dim, dim)
        self.heads = heads
        self.dropout = nn.Dropout(dropout)

    def forward(self, x):
        residual = x
        x = self.norm(x)
        b, d = x.shape
        h = self.heads
        hd = d // h
        q = self.q(x).view(b, h, hd)
        k = self.k(x).view(b, h, hd)
        v = self.v(x).view(b, h, hd)
        attn = torch.softmax((q * k) / math.sqrt(hd), dim=-1)
        out = (attn * v).reshape(b, d)
        out = self.proj(out)
        out = self.dropout(out)
        return out + residual

class DeepTornadoRegressor(nn.Module):
    def __init__(self, input_dim, output_dim):
        super().__init__()
        self.input_proj = nn.Sequential(
            nn.Linear(input_dim, 256),
            nn.GELU(),
            nn.BatchNorm1d(256),
            nn.Dropout(0.15),
            nn.Linear(256, 384),
            nn.GELU(),
            nn.BatchNorm1d(384),
            nn.Dropout(0.15)
        )

        self.block1 = GatedFeatureBlock(384, expansion=2, dropout=0.12)
        self.mix1 = CrossFeatureMixer(384, heads=6, dropout=0.1)
        self.block2 = GatedFeatureBlock(384, expansion=3, dropout=0.12)
        self.mix2 = CrossFeatureMixer(384, heads=6, dropout=0.1)
        self.block3 = GatedFeatureBlock(384, expansion=2, dropout=0.12)

        self.mid = nn.Sequential(
            nn.LayerNorm(384),
            nn.Linear(384, 256),
            nn.GELU(),
            nn.Dropout(0.12),
            nn.Linear(256, 192),
            nn.GELU(),
            nn.Dropout(0.1)
        )

        self.wind_head = nn.Sequential(
            nn.Linear(192, 96),
            nn.GELU(),
            nn.Linear(96, 1)
        )
        self.radius_head = nn.Sequential(
            nn.Linear(192, 96),
            nn.GELU(),
            nn.Linear(96, 1)
        )
        self.debris_head = nn.Sequential(
            nn.Linear(192, 96),
            nn.GELU(),
            nn.Linear(96, 1)
        )
        self.pressure_head = nn.Sequential(
            nn.Linear(192, 96),
            nn.GELU(),
            nn.Linear(96, 1)
        )
        self.angular_head = nn.Sequential(
            nn.Linear(192, 96),
            nn.GELU(),
            nn.Linear(96, 1)
        )

        self.fusion = nn.Sequential(
            nn.Linear(5, 32),
            nn.GELU(),
            nn.Linear(32, output_dim)
        )

    def forward(self, x):
        x = self.input_proj(x)
        x = self.block1(x)
        x = self.mix1(x)
        x = self.block2(x)
        x = self.mix2(x)
        x = self.block3(x)
        x = self.mid(x)

        y1 = self.wind_head(x)
        y2 = self.radius_head(x)
        y3 = self.debris_head(x)
        y4 = self.pressure_head(x)
        y5 = self.angular_head(x)

        y = torch.cat([y1, y2, y3, y4, y5], dim=1)
        y = self.fusion(y)
        return y

def load_data():
    if os.path.exists(CSV_PATH):
        df = pd.read_csv(CSV_PATH)
    else:
        df = make_synthetic_dataframe()

    df = feature_engineering(df)

    feature_cols = INPUT_COLUMNS + [
        "wind_dir_sin",
        "wind_dir_cos",
        "wind_vector_mag",
        "temp_dew_spread",
        "instability_ratio",
        "shear_ratio",
        "moisture_energy",
        "terrain_wind_interaction",
        "storm_spin_proxy",
        "updraft_shear_proxy",
        "geo_lat_lon",
        "pressure_deficit"
    ]

    x = df[feature_cols].values
    y = df[TARGET_COLUMNS].values

    x_train, x_temp, y_train, y_temp = train_test_split(x, y, test_size=0.25, random_state=42)
    x_val, x_test, y_val, y_test = train_test_split(x_temp, y_temp, test_size=0.5, random_state=42)

    x_scaler = StandardScaler()
    y_scaler = StandardScaler()

    x_train = x_scaler.fit_transform(x_train)
    x_val = x_scaler.transform(x_val)
    x_test = x_scaler.transform(x_test)

    y_train = y_scaler.fit_transform(y_train)
    y_val = y_scaler.transform(y_val)
    y_test = y_scaler.transform(y_test)

    train_ds = TornadoDataset(x_train, y_train)
    val_ds = TornadoDataset(x_val, y_val)
    test_ds = TornadoDataset(x_test, y_test)

    train_loader = DataLoader(train_ds, batch_size=BATCH_SIZE, shuffle=True, drop_last=False)
    val_loader = DataLoader(val_ds, batch_size=BATCH_SIZE, shuffle=False, drop_last=False)
    test_loader = DataLoader(test_ds, batch_size=BATCH_SIZE, shuffle=False, drop_last=False)

    return train_loader, val_loader, test_loader, x_scaler, y_scaler, feature_cols

def evaluate(model, loader, y_scaler):
    model.eval()
    preds = []
    trues = []
    loss_total = 0.0

    with torch.no_grad():
        for xb, yb in loader:
            xb = xb.to(DEVICE)
            yb = yb.to(DEVICE)
            out = model(xb)
            loss = F.mse_loss(out, yb)
            loss_total += loss.item() * xb.size(0)
            preds.append(out.cpu().numpy())
            trues.append(yb.cpu().numpy())

    preds = np.vstack(preds)
    trues = np.vstack(trues)

    preds_real = y_scaler.inverse_transform(preds)
    trues_real = y_scaler.inverse_transform(trues)

    metrics = {}
    metrics["loss"] = loss_total / len(loader.dataset)
    metrics["mae"] = mean_absolute_error(trues_real, preds_real, multioutput="uniform_average")
    metrics["rmse"] = np.sqrt(mean_squared_error(trues_real, preds_real))
    metrics["r2"] = r2_score(trues_real, preds_real, multioutput="uniform_average")
    return metrics, preds_real, trues_real

def train():
    train_loader, val_loader, test_loader, x_scaler, y_scaler, feature_cols = load_data()

    model = DeepTornadoRegressor(input_dim=len(feature_cols), output_dim=len(TARGET_COLUMNS)).to(DEVICE)
    optimizer = torch.optim.AdamW(model.parameters(), lr=LEARNING_RATE, weight_decay=WEIGHT_DECAY)
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=EPOCHS)
    best_val = float("inf")
    best_state = None

    for epoch in range(1, EPOCHS + 1):
        model.train()
        running = 0.0

        for xb, yb in train_loader:
            xb = xb.to(DEVICE)
            yb = yb.to(DEVICE)

            optimizer.zero_grad()
            out = model(xb)

            mse = F.mse_loss(out, yb)
            l1 = F.l1_loss(out, yb)
            huber = F.smooth_l1_loss(out, yb)
            loss = 0.6 * mse + 0.25 * l1 + 0.15 * huber

            loss.backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), max_norm=2.0)
            optimizer.step()

            running += loss.item() * xb.size(0)

        scheduler.step()

        train_loss = running / len(train_loader.dataset)
        val_metrics, _, _ = evaluate(model, val_loader, y_scaler)

        if val_metrics["loss"] < best_val:
            best_val = val_metrics["loss"]
            best_state = {k: v.cpu().clone() for k, v in model.state_dict().items()}

        print(
            f"Epoch {epoch:03d} | "
            f"Train Loss: {train_loss:.4f} | "
            f"Val Loss: {val_metrics['loss']:.4f} | "
            f"Val MAE: {val_metrics['mae']:.4f} | "
            f"Val RMSE: {val_metrics['rmse']:.4f} | "
            f"Val R2: {val_metrics['r2']:.4f}"
        )

    if best_state is not None:
        model.load_state_dict(best_state)

    test_metrics, preds_real, trues_real = evaluate(model, test_loader, y_scaler)

    print("\nFinal Test Metrics")
    print(f"Loss: {test_metrics['loss']:.4f}")
    print(f"MAE:  {test_metrics['mae']:.4f}")
    print(f"RMSE: {test_metrics['rmse']:.4f}")
    print(f"R2:   {test_metrics['r2']:.4f}")

    results = pd.DataFrame(
        np.hstack([trues_real, preds_real]),
        columns=[f"true_{c}" for c in TARGET_COLUMNS] + [f"pred_{c}" for c in TARGET_COLUMNS]
    )
    results.to_csv("tornado_model_predictions.csv", index=False)
    torch.save(model.state_dict(), "deep_tornado_regressor.pt")

    sample_inputs = pd.DataFrame({
        "latitude": [35.4, 33.2, 41.8],
        "longitude": [-97.5, -86.7, -92.4],
        "elevation": [380, 120, 620],
        "temperature": [29, 31, 24],
        "dewpoint": [22, 24, 17],
        "humidity": [72, 80, 61],
        "pressure": [992, 986, 1001],
        "wind_u": [12, 18, 9],
        "wind_v": [8, 11, 5],
        "wind_speed_surface": [14.4, 21.1, 10.7],
        "wind_speed_mid": [33.0, 40.5, 25.6],
        "wind_direction": [210, 190, 225],
        "cape": [2600, 4100, 1800],
        "cin": [45, 22, 70],
        "srh": [240, 380, 170],
        "lifted_index": [-6, -9, -4],
        "shear_0_1km": [24, 32, 18],
        "shear_0_6km": [48, 61, 39],
        "precipitable_water": [1.6, 2.1, 1.2],
        "lcl_height": [900, 700, 1100],
        "terrain_roughness": [0.32, 0.41, 0.25],
        "land_cover_index": [0.56, 0.63, 0.49],
        "storm_motion": [38, 46, 29],
        "vorticity_index": [0.62, 0.81, 0.43],
        "updraft_index": [0.58, 0.74, 0.39]
    })

    sample_inputs = feature_engineering(sample_inputs)
    feature_cols = [c for c in sample_inputs.columns]
    sample_x = x_scaler.transform(sample_inputs[feature_cols].values)
    sample_x = torch.tensor(sample_x, dtype=torch.float32).to(DEVICE)

    model.eval()
    with torch.no_grad():
        sample_pred = model(sample_x).cpu().numpy()

    sample_pred = y_scaler.inverse_transform(sample_pred)
    sample_df = pd.DataFrame(sample_pred, columns=TARGET_COLUMNS)
    print("\nDemo Predictions")
    print(sample_df.round(3).to_string(index=False))

if __name__ == "__main__":
    train()