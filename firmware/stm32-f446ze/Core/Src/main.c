/* USER CODE BEGIN Header */
/**
 ******************************************************************************
 * @file           : main.c
 * @brief          : Main program body
 ******************************************************************************
 * @attention
 *
 * Copyright (c) 2026 STMicroelectronics.
 * All rights reserved.
 *
 * This software is licensed under terms that can be found in the LICENSE file
 * in the root directory of this software component.
 * If no LICENSE file comes with this software, it is provided AS-IS.
 *
 ******************************************************************************
 */
/* USER CODE END Header */
/* Includes ------------------------------------------------------------------*/
#include "main.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <math.h>
/* USER CODE END Includes */

/* Private typedef -----------------------------------------------------------*/
/* USER CODE BEGIN PTD */

/* USER CODE END PTD */

/* Private define ------------------------------------------------------------*/
/* USER CODE BEGIN PD */

#define EVENT_SAMPLE_COUNT 32
#define EVENT_CHANNEL_COUNT 60
#define LUT_FRAME_COUNT 1
#define ADC_MIN 0
#define ADC_MAX 4095

#define FRAME_HEADER_BYTES (2 + 4 + 4 + 2 + 2)
#define FRAME_SAMPLE_BYTES (EVENT_CHANNEL_COUNT * EVENT_SAMPLE_COUNT * 2)
#define FRAME_PARAM_BYTES  (EVENT_CHANNEL_COUNT * 12)
#define FRAME_BYTES        (FRAME_HEADER_BYTES + FRAME_SAMPLE_BYTES + FRAME_PARAM_BYTES)

/* USER CODE END PD */

/* Private macro -------------------------------------------------------------*/
/* USER CODE BEGIN PM */

/* USER CODE END PM */

/* Private variables ---------------------------------------------------------*/
UART_HandleTypeDef huart3;

/* USER CODE BEGIN PV */

/* USER CODE END PV */

/* Private function prototypes -----------------------------------------------*/
void SystemClock_Config(void);
static void MX_GPIO_Init(void);
static void MX_USART3_UART_Init(void);
/* USER CODE BEGIN PFP */

/* USER CODE END PFP */

/* Private user code ---------------------------------------------------------*/
/* USER CODE BEGIN 0 */

static uint16_t clamp_adc(int value)
{
  if (value < ADC_MIN)
    return ADC_MIN;
  if (value > ADC_MAX)
    return ADC_MAX;
  return (uint16_t)value;
}

// Pre-computed LUT: K reference frames, each with samples + params blocks
// matching the wire format layout exactly so we can memcpy them in one shot.
static uint8_t lut_samples[LUT_FRAME_COUNT][FRAME_SAMPLE_BYTES];
static uint8_t lut_params[LUT_FRAME_COUNT][FRAME_PARAM_BYTES];

// Per-channel waveform shape types
#define SHAPE_NOISE_ONLY   0
#define SHAPE_FAST_PULSE   1   // PMT/SiPM-like: sharp rise + exp decay
#define SHAPE_GAUSSIAN     2   // CR-RC^n shaper output
#define SHAPE_PREAMP       3   // Charge-sensitive preamp: step + slow RC tail
#define SHAPE_BIPOLAR      4   // Derivative-of-Gaussian: positive then negative lobe
#define SHAPE_PILEUP       5   // Two pulses close in time
#define SHAPE_SATURATED    6   // Above ADC range — clipped flat-top
#define SHAPE_DAMPED_OSC   7   // Detector ringing
#define SHAPE_SINUSOIDAL   8   // Multi-period sine (pickup)
#define SHAPE_PULSE_TRAIN  9   // Bunch-structure: periodic pulses
#define SHAPE_PICKUP_60HZ  10  // Low-amp sinusoidal background

// Returns signed signal value to add to baseline (samples_idx in [0, 32)).
static int synth_signal(int sample_idx, int shape, int amp, int center)
{
  switch (shape)
  {
    case SHAPE_FAST_PULSE: {
      if (sample_idx < center) return 0;
      float dx = (float)(sample_idx - center);
      return (int)(amp * expf(-dx / 4.0f));
    }
    case SHAPE_GAUSSIAN: {
      float dx = (float)(sample_idx - center);
      float sigma = 3.0f;
      return (int)(amp * expf(-(dx * dx) / (2.0f * sigma * sigma)));
    }
    case SHAPE_PREAMP: {
      if (sample_idx < center - 1) return 0;
      if (sample_idx <= center + 1) return amp;
      float dx = (float)(sample_idx - center - 1);
      return (int)(amp * expf(-dx / 14.0f));
    }
    case SHAPE_BIPOLAR: {
      float dx = (float)(sample_idx - center);
      float sigma = 2.5f;
      // First derivative of Gaussian: -dx/sigma * exp(-dx^2 / 2 sigma^2)
      return (int)(-amp * (dx / sigma) * expf(-(dx * dx) / (2.0f * sigma * sigma)));
    }
    case SHAPE_PILEUP: {
      int c2 = center + 8;
      float sigma = 2.0f;
      float d1 = (float)(sample_idx - center);
      float d2 = (float)(sample_idx - c2);
      int s1 = (int)(amp * expf(-(d1 * d1) / (2.0f * sigma * sigma)));
      int s2 = (int)((amp * 2 / 3) * expf(-(d2 * d2) / (2.0f * sigma * sigma)));
      return s1 + s2;
    }
    case SHAPE_SATURATED: {
      // Wide pulse intentionally above ADC range — clamp_adc trims
      int width = 8;
      int dist = sample_idx - center;
      if (dist < 0) dist = -dist;
      if (dist < width) return amp * 2;
      return 0;
    }
    case SHAPE_DAMPED_OSC: {
      if (sample_idx < center) return 0;
      float dx = (float)(sample_idx - center);
      return (int)(amp * expf(-dx / 8.0f) * cosf(dx * 0.7f));
    }
    case SHAPE_SINUSOIDAL: {
      // Two periods across the 32-sample window
      float dx = (float)sample_idx;
      return (int)(amp * sinf(dx * 6.2832f / 16.0f));
    }
    case SHAPE_PULSE_TRAIN: {
      // Sharp pulses every 8 samples (bunch-structure-like)
      int phase = sample_idx % 8;
      if (phase == 0) return amp;
      if (phase == 1) return amp / 2;
      return 0;
    }
    case SHAPE_PICKUP_60HZ: {
      // ~3 periods of low-amp sine across window
      float dx = (float)sample_idx;
      return (int)(amp * sinf(dx * 6.2832f / 10.0f));
    }
    case SHAPE_NOISE_ONLY:
    default:
      return 0;
  }
}

// Each channel has ONE characteristic physics-pulse shape (fixed for the channel's
// lifetime). Channels rotate through the 7 shapes so adjacent channels look distinct,
// but the channel→shape mapping never changes. Per-event variation comes purely
// from amp/baseline/timing jitter applied in the hot path.
static int channel_shape(int channel_id)
{
  static const int physics_shapes[] = {
    SHAPE_GAUSSIAN,    // CR-RC shaper output
    SHAPE_FAST_PULSE,  // PMT/SiPM-like
    SHAPE_PREAMP,      // Charge-sensitive preamp
    SHAPE_BIPOLAR,     // CR-RC^2 shaper
    SHAPE_PILEUP,      // Two close pulses
    SHAPE_DAMPED_OSC,  // Detector ringing
    SHAPE_SATURATED,   // Above ADC range
  };
  return physics_shapes[channel_id % (sizeof(physics_shapes) / sizeof(physics_shapes[0]))];
}

// Fast PRNG for hot-path noise. xorshift32 is ~10 cycles vs ~100 for libc rand().
static uint32_t prng_state = 0x1234567Au;
static inline uint32_t prng_next(void)
{
  uint32_t x = prng_state;
  x ^= x << 13;
  x ^= x >> 17;
  x ^= x << 5;
  prng_state = x;
  return x;
}

// Per-channel reference values, set once at boot. Each channel keeps its own
// baseline, amplitude, and pulse-center for its lifetime. Per-event jitter
// is applied on top of these so the channel's identity stays consistent
// while parameters vary continuously across events.
static uint16_t channel_baseline[EVENT_CHANNEL_COUNT];

// LUT holds the CLEAN signal for each channel — that channel's chosen physics
// shape rendered with its fixed baseline/amp/center. One frame total: each
// channel's identity is permanent; per-event variation comes from jitter, not
// from cycling through different LUT frames.
static void generate_lut(void)
{
  uint16_t *samples = (uint16_t *)lut_samples[0];
  uint8_t  *params  = lut_params[0];

  for (int c = 0; c < EVENT_CHANNEL_COUNT; c++)
  {
    int shape = channel_shape(c);
    uint16_t baseline = clamp_adc(1400 + (rand() % 201));      // 1400..1600 per channel
    int amp = 1600 + (rand() % 1400);                           // 1600..3000 per channel
    int center = 10 + (rand() % 10);                            // 10..19 per channel

    channel_baseline[c] = baseline;

    uint16_t *ch_samples = samples + c * EVENT_SAMPLE_COUNT;
    for (int i = 0; i < EVENT_SAMPLE_COUNT; i++)
    {
      int signal = synth_signal(i, shape, amp, center);
      ch_samples[i] = clamp_adc((int)baseline + signal);
    }

    // Only baseline matters in the LUT params block — other params are recomputed
    // per event from jittered samples.
    uint8_t *p = params + c * 12;
    memcpy(p + 0, &baseline, 2);
    memset(p + 2, 0, 10);
  }
}

// Per-event hot path: copy clean samples from LUT, apply jitter (per-event amp scale,
// baseline shift, per-sample noise), recompute all four params from the jittered samples.
// Channel identity (shape, baseline, amp scale center) stays fixed; only the *values*
// vary event to event, so histograms become smooth distributions.
static inline void apply_jitter_and_compute_params(uint8_t *frame_buf)
{
  const uint16_t *lut = (const uint16_t *)lut_samples[0];
  uint16_t *frame_samples = (uint16_t *)(frame_buf + FRAME_HEADER_BYTES);
  uint8_t *frame_p = frame_buf + FRAME_HEADER_BYTES + FRAME_SAMPLE_BYTES;

  for (int c = 0; c < EVENT_CHANNEL_COUNT; c++)
  {
    const uint16_t *src = lut + c * EVENT_SAMPLE_COUNT;
    uint16_t *dst = frame_samples + c * EVENT_SAMPLE_COUNT;
    uint16_t base = channel_baseline[c];

    // Per-event amplitude scale: 0.25..2.0 (Q8 fixed point: 64..512).
    // Wide enough that Area / PeakHeight span ~a decade on a log axis,
    // so histograms read as proper bell curves instead of single-bin spikes.
    uint32_t amp_q8 = 64 + (prng_next() % 449);

    // Per-event baseline scale: multiplicative 0.25..2.0 (Q8: 64..512).
    // Multiplicative jitter → values span ~a decade on a log axis instead
    // of clustering in one bin around the channel's nominal baseline.
    uint32_t base_q8 = 64 + (prng_next() % 449);
    uint16_t event_baseline = clamp_adc(((int)base * (int)base_q8) >> 8);

    uint16_t peak_height = 0;
    uint32_t area = 0;

    // Pass 1: jitter + accumulate peak_height & area
    for (int i = 0; i < EVENT_SAMPLE_COUNT; i++)
    {
      int signal_clean = (int)src[i] - (int)base;        // signal above original baseline
      int signal_scaled = (signal_clean * (int)amp_q8) >> 8;  // amp jitter
      int s = (int)event_baseline + signal_scaled;

      // Per-sample noise: ~±64 ADC counts
      int noise = ((int)(prng_next() & 0xFF)) - 128;
      s += noise / 2;

      if (s < ADC_MIN) s = ADC_MIN;
      if (s > ADC_MAX) s = ADC_MAX;
      dst[i] = (uint16_t)s;
      if (s > peak_height) peak_height = (uint16_t)s;
      if (s > event_baseline) area += (uint32_t)(s - event_baseline);
    }

    // Pass 2: peak_width = "area above half-height" = sum of (sample - threshold)
    // for samples above the half-max threshold. Real instruments report this
    // as FWHM × amplitude; mathematically it scales with both pulse width
    // (in samples) AND pulse height, so values span 0..~30k and read as a
    // proper distribution across multiple decades on a log axis.
    uint16_t threshold = event_baseline + (peak_height - event_baseline) / 2;
    uint32_t peak_width = 0;
    for (int i = 0; i < EVENT_SAMPLE_COUNT; i++)
    {
      if (dst[i] >= threshold)
        peak_width += (uint32_t)(dst[i] - threshold);
    }

    // Report peak HEIGHT ABOVE BASELINE (standard PHA convention) rather than
    // the raw max sample. Raw samples are clamped to ADC_MAX (4095) which
    // squashes the upper tail of the distribution; subtracting baseline lets
    // the multiplicative amp jitter spread peak_height across decades.
    peak_height = (peak_height > event_baseline)
        ? (uint16_t)(peak_height - event_baseline)
        : 0;

    // Write fresh params with the event's actual baseline + recomputed values
    uint8_t *p = frame_p + c * 12;
    memcpy(p + 0,  &event_baseline, 2);
    memcpy(p + 2,  &area,           4);
    memcpy(p + 6,  &peak_width,     4);
    memcpy(p + 10, &peak_height,    2);
  }
}

/* USER CODE END 0 */

/**
  * @brief  The application entry point.
  * @retval int
  */
int main(void)
{

  /* USER CODE BEGIN 1 */

  /* USER CODE END 1 */

  /* MCU Configuration--------------------------------------------------------*/

  /* Reset of all peripherals, Initializes the Flash interface and the Systick. */
  HAL_Init();

  /* USER CODE BEGIN Init */

  /* USER CODE END Init */

  /* Configure the system clock */
  SystemClock_Config();

  /* USER CODE BEGIN SysInit */

  /* USER CODE END SysInit */

  /* Initialize all configured peripherals */
  MX_GPIO_Init();
  MX_USART3_UART_Init();
  /* USER CODE BEGIN 2 */
  // Wire frame:
  //   [0xA5][0x5A][event_id u32][timestamp u32][channel_count u16][sample_count u16]
  //   [samples u16[C*N] LE]
  //   [params per channel: baseline u16, area u32, peakWidth u32, peakHeight u16]
  static uint8_t frame[FRAME_BYTES];

  // Static header fields
  frame[0] = 0xA5;
  frame[1] = 0x5A;
  uint16_t channel_count = EVENT_CHANNEL_COUNT;
  uint16_t sample_count = EVENT_SAMPLE_COUNT;
  memcpy(frame + 10, &channel_count, sizeof(channel_count));
  memcpy(frame + 12, &sample_count, sizeof(sample_count));

  // Pre-compute K LUTs once (~18 KB total)
  generate_lut();

  uint32_t event_id = 0;

  /* USER CODE END 2 */

  /* Infinite loop */
  /* USER CODE BEGIN WHILE */
  while (1)
  {
    /* USER CODE END WHILE */

    /* USER CODE BEGIN 3 */
    uint32_t timestamp = HAL_GetTick();

    // Apply per-event jitter (amp scale + baseline shift + per-sample noise) on top
    // of each channel's clean LUT samples, then recompute all four params from the
    // jittered samples. Channel identity (shape) is permanent; the *values* vary
    // event to event, giving smooth histogram distributions.
    apply_jitter_and_compute_params(frame);

    // Per-event header updates
    memcpy(frame + 2, &event_id, sizeof(event_id));
    memcpy(frame + 6, &timestamp, sizeof(timestamp));

    HAL_UART_Transmit(&huart3, frame, sizeof(frame), HAL_MAX_DELAY);

    event_id++;
  }
  /* USER CODE END 3 */
}

/**
  * @brief System Clock Configuration
  * @retval None
  */
void SystemClock_Config(void)
{
  RCC_OscInitTypeDef RCC_OscInitStruct = {0};
  RCC_ClkInitTypeDef RCC_ClkInitStruct = {0};

  /** Configure the main internal regulator output voltage
  */
  __HAL_RCC_PWR_CLK_ENABLE();
  __HAL_PWR_VOLTAGESCALING_CONFIG(PWR_REGULATOR_VOLTAGE_SCALE3);

  /** Initializes the RCC Oscillators according to the specified parameters
  * in the RCC_OscInitTypeDef structure.
  */
  RCC_OscInitStruct.OscillatorType = RCC_OSCILLATORTYPE_HSI;
  RCC_OscInitStruct.HSIState = RCC_HSI_ON;
  RCC_OscInitStruct.HSICalibrationValue = RCC_HSICALIBRATION_DEFAULT;
  RCC_OscInitStruct.PLL.PLLState = RCC_PLL_ON;
  RCC_OscInitStruct.PLL.PLLSource = RCC_PLLSOURCE_HSI;
  RCC_OscInitStruct.PLL.PLLM = 8;
  RCC_OscInitStruct.PLL.PLLN = 84;
  RCC_OscInitStruct.PLL.PLLP = RCC_PLLP_DIV2;
  RCC_OscInitStruct.PLL.PLLQ = 2;
  RCC_OscInitStruct.PLL.PLLR = 2;
  if (HAL_RCC_OscConfig(&RCC_OscInitStruct) != HAL_OK)
  {
    Error_Handler();
  }

  /** Initializes the CPU, AHB and APB buses clocks
  */
  RCC_ClkInitStruct.ClockType = RCC_CLOCKTYPE_HCLK|RCC_CLOCKTYPE_SYSCLK
                              |RCC_CLOCKTYPE_PCLK1|RCC_CLOCKTYPE_PCLK2;
  RCC_ClkInitStruct.SYSCLKSource = RCC_SYSCLKSOURCE_PLLCLK;
  RCC_ClkInitStruct.AHBCLKDivider = RCC_SYSCLK_DIV1;
  RCC_ClkInitStruct.APB1CLKDivider = RCC_HCLK_DIV2;
  RCC_ClkInitStruct.APB2CLKDivider = RCC_HCLK_DIV1;

  if (HAL_RCC_ClockConfig(&RCC_ClkInitStruct, FLASH_LATENCY_2) != HAL_OK)
  {
    Error_Handler();
  }
}

/**
  * @brief USART3 Initialization Function
  * @param None
  * @retval None
  */
static void MX_USART3_UART_Init(void)
{

  /* USER CODE BEGIN USART3_Init 0 */

  /* USER CODE END USART3_Init 0 */

  /* USER CODE BEGIN USART3_Init 1 */

  /* USER CODE END USART3_Init 1 */
  huart3.Instance = USART3;
  huart3.Init.BaudRate = 2000000;
  huart3.Init.WordLength = UART_WORDLENGTH_8B;
  huart3.Init.StopBits = UART_STOPBITS_1;
  huart3.Init.Parity = UART_PARITY_NONE;
  huart3.Init.Mode = UART_MODE_TX_RX;
  huart3.Init.HwFlowCtl = UART_HWCONTROL_NONE;
  huart3.Init.OverSampling = UART_OVERSAMPLING_16;
  if (HAL_UART_Init(&huart3) != HAL_OK)
  {
    Error_Handler();
  }
  /* USER CODE BEGIN USART3_Init 2 */

  /* USER CODE END USART3_Init 2 */

}

/**
  * @brief GPIO Initialization Function
  * @param None
  * @retval None
  */
static void MX_GPIO_Init(void)
{
  GPIO_InitTypeDef GPIO_InitStruct = {0};
  /* USER CODE BEGIN MX_GPIO_Init_1 */

  /* USER CODE END MX_GPIO_Init_1 */

  /* GPIO Ports Clock Enable */
  __HAL_RCC_GPIOB_CLK_ENABLE();
  __HAL_RCC_GPIOD_CLK_ENABLE();

  /*Configure GPIO pin Output Level */
  HAL_GPIO_WritePin(GPIOB, GPIO_PIN_0|GPIO_PIN_14|GPIO_PIN_7, GPIO_PIN_RESET);

  /*Configure GPIO pins : PB0 PB14 PB7 */
  GPIO_InitStruct.Pin = GPIO_PIN_0|GPIO_PIN_14|GPIO_PIN_7;
  GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(GPIOB, &GPIO_InitStruct);

  /* USER CODE BEGIN MX_GPIO_Init_2 */

  /* USER CODE END MX_GPIO_Init_2 */
}

/* USER CODE BEGIN 4 */

/* USER CODE END 4 */

/**
  * @brief  This function is executed in case of error occurrence.
  * @retval None
  */
void Error_Handler(void)
{
  /* USER CODE BEGIN Error_Handler_Debug */
  /* User can add his own implementation to report the HAL error return state */
  __disable_irq();
  while (1)
  {
  }
  /* USER CODE END Error_Handler_Debug */
}
#ifdef USE_FULL_ASSERT
/**
  * @brief  Reports the name of the source file and the source line number
  *         where the assert_param error has occurred.
  * @param  file: pointer to the source file name
  * @param  line: assert_param error line source number
  * @retval None
  */
void assert_failed(uint8_t *file, uint32_t line)
{
  /* USER CODE BEGIN 6 */
  /* User can add his own implementation to report the file name and line number,
     ex: printf("Wrong parameters value: file %s on line %d\r\n", file, line) */
  /* USER CODE END 6 */
}
#endif /* USE_FULL_ASSERT */
