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
/* USER CODE END Includes */

/* Private typedef -----------------------------------------------------------*/
/* USER CODE BEGIN PTD */

/* USER CODE END PTD */

/* Private define ------------------------------------------------------------*/
/* USER CODE BEGIN PD */

#define EVENT_SAMPLE_COUNT 32
#define EVENT_CHANNEL_COUNT 60
#define LUT_FRAME_COUNT 4
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

static void generate_lut(void)
{
  for (int f = 0; f < LUT_FRAME_COUNT; f++)
  {
    uint16_t *samples = (uint16_t *)lut_samples[f];
    uint8_t  *params  = lut_params[f];

    for (int c = 0; c < EVENT_CHANNEL_COUNT; c++)
    {
      uint16_t baseline = clamp_adc(1500 + (rand() % 101) - 50);
      int amp = 800 + rand() % 1800;
      int center = 10 + rand() % 12;
      int width = 6 + rand() % 10;

      uint16_t peak_height = 0;
      uint32_t area = 0;
      uint16_t *ch_samples = samples + c * EVENT_SAMPLE_COUNT;

      for (int i = 0; i < EVENT_SAMPLE_COUNT; i++)
      {
        int distance = i - center;
        if (distance < 0) distance = -distance;
        int pulse = (distance < width) ? amp * (width - distance) / width : 0;
        int noise = (rand() % 81) - 40;
        ch_samples[i] = clamp_adc((int)baseline + pulse + noise);
        if (ch_samples[i] > peak_height) peak_height = ch_samples[i];
        if (ch_samples[i] > baseline) area += ch_samples[i] - baseline;
      }

      uint16_t threshold = baseline + (peak_height - baseline) / 2;
      uint32_t peak_width = 0;
      for (int i = 0; i < EVENT_SAMPLE_COUNT; i++)
      {
        if (ch_samples[i] >= threshold) peak_width++;
      }

      // Pack params for this channel: baseline u16, area u32, peakWidth u32, peakHeight u16
      uint8_t *p = params + c * 12;
      memcpy(p + 0, &baseline,   2);
      memcpy(p + 2, &area,       4);
      memcpy(p + 6, &peak_width, 4);
      memcpy(p + 10, &peak_height, 2);
    }
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
    int frame_idx = event_id & (LUT_FRAME_COUNT - 1);

    // Copy pre-cooked samples + params blocks
    memcpy(frame + FRAME_HEADER_BYTES, lut_samples[frame_idx], FRAME_SAMPLE_BYTES);
    memcpy(frame + FRAME_HEADER_BYTES + FRAME_SAMPLE_BYTES, lut_params[frame_idx], FRAME_PARAM_BYTES);

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
